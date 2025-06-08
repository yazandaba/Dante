# Dante

Dante is a model checker for C# code transformations, built to formally verify the equivalence of program transformations.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
![Status: Early Alpha](https://img.shields.io/badge/Status-Early%20Alpha-orange)
![Version: 0.0.1](https://img.shields.io/badge/Version-0.0.1-brightgreen)

---
## Overview

Dante enables the formal verification of C# code transformations by compiling Control Flow Graphs (CFGs) from C# programs into Z3 formulas. It meticulously checks if a function's behavior remains identical across all possible inputs after a transformation.

Unlike many tools that operate on low-level Intermediate Representation (IR) like .NET IL (MSIL) or native binaries, Dante works at a higher level using Roslyn's bound tree operations. This approach simplifies usage by eliminating the need for a full compilation cycle to verify the targeted code, though it adds complexity to Dante's internal implementation.

The core compiler utilizes Microsoft's Roslyn C# CFGs, translating them into Z3 formulas in functional Static Single Assignment (SSA) form (also known as Basic Block Argument form). By systematically exploring the input space that could reveal discrepancies between two program versions, Dante helps pinpoint potential flaws in code transformations.

The verification process is unbounded, terminating based on resource limits or a timeout. Consequently, users can anticipate one of three standard outcomes for satisfiability problems:
* `sat`: Satisfiable, indicating a counterexample to equivalence was found.
* `unsat`: Unsatisfiable, meaning equivalence is proven (within the checked bounds/assumptions).
* `unknown`: The solver timed out or reached a resource limit before reaching a definitive conclusion.

---
## Features

* **Equivalence Checking**: Verifies that two program versions (e.g., A and A') are semantically equivalent. It does this by tasking an SMT solver to find any input that would cause them to produce different outputs, thereby breaking the bi-implication A <=> A'.
* **C# Language Support**:
    * Primitive types (e.g., `int`, `long`, `uint`, `string`)
    * `IEnumerable`, Arrays, and nullable types
    * Control flow constructs (If/else, For loops, While loops)
    * Recursive functions
    * Lambda expressions
    * Partial LINQ support (method syntax: `Select`, `Where`, `Take`, `ToArray`)
* **Interprocedural Analysis**:
    * Generates a full call graph starting from the provided entry functions. This capability is currently limited to functions and methods within the same lexical scope (i.e., originating from the same class or struct).

---
## Status

⚠️ The project is currently in an **early alpha** stage. It has primarily undergone manual testing, so you might encounter number of bugs. also the performance of the core compiler can be drastically improved and finally support is currently limited to a subset of the C# language.

---
## Requirements

* .NET 8.0 SDK or newer

---
## Installation

1.  **Clone the repository:**
    ```bash
    git clone https://github.com/yazandaba/Dante.git
    cd Dante
    ```
2.  **Build the project:**
    ```bash
    dotnet build
    ```
    For macOS users, please see the specific Z3 setup instructions in the "Platform Support" section below *before* building if you encounter issues related to Z3.

---
## Platform Support

* **Windows**: Works out of the box.
* **AArch64-based macOS**: Requires additional setup for Z3. The standard Z3 NuGet packages may not function correctly.
    1.  **Compile Z3 from source**: Follow the official Z3 documentation to compile it on your macOS system (Apple Clang or LLVM Clang are suitable compilers).
    2.  **Configure local NuGet source**: Create a `nuget.config` file at the root of the Dante project (next to the `.sln` file) with the following content. **Important**: You must replace `/Users/user/path/to/z3/bin/...` with the actual local path to the `bin` directory where your compiled Z3 binaries are located.
        ```xml
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
            <packageSources>
                <add key="nuget.org" value="[https://api.nuget.org/v3/index.json](https://api.nuget.org/v3/index.json)" />
                <add key="local" value="/Users/user/path/to/z3/bin/..." />
            </packageSources>
        </configuration>
        ```
        After these steps, try building the Dante project again.

---
## Usage

Dante analyzes a function before and after a transformation to confirm they produce identical outputs for all possible inputs.

### Example 1: Constant Propagation

Consider the following constant propagation optimization:

```csharp
// Original version
public string Original(string? x)
{
    if (x == null)
    {
        x = "Hello";
    }
    else
    {
        x = "Hello";
    }

    return x+"World";
}

// Transformed version
public string Transformed(string? x)
{
    return "HelloWorld";
}
```

Dante can verify that these two implementations are equivalent.

**To run this example:**

```bash
Dante.exe -highlight -debug -p Sample.csproj -c Sample.BasicControlFlow -original Original -transformed Transformed -r 10
```

**Output:**

```
Message:original function always imply transformed function, there was no of set values that could be assigned to the abstract symbols where it breaks the bi-proposition.
=========================================
```

```lisp
SMT
(declare-datatypes ((MaybeString 0)) (((None) (Some (value String)))))
(declare-fun param!0 () MaybeString)
(define-funs-rec ( ( BB1000 ((x!1 MaybeString)) String)
                   ( BB1001 () String)
                   ( BB0000 ((x!1 MaybeString)) String)
                   ( BB0001 ((x!1 MaybeString)) String)
                   ( BB0005 ((x!1 MaybeString)) String)
                   ( BB0003 ((x!1 MaybeString)) String)
                   ( BB0002 ((x!1 MaybeString)) String))
                 ( (_ BB1001 0)
                   "HelloWorld"
                   ((_ BB0001 0) x!1)
                   (ite ((_ is None) x!1) ((_ BB0002 0) x!1) ((_ BB0005 0) x!1))
                   ((_ BB0003 0) (Some "Hello"))
                   (str.++ (value x!1) "World")
                   ((_ BB0003 0) (Some "Hello"))))
(assert (not (= ((_ BB0000 0) param!0) ((_ BB1000 0) param!0))))
```

This output indicates:
1.  The SMT solver proved that the transformed function is equivalent to the original.
2.  The SMT formula generated for the verification is displayed.

### Example 2: Incorrect LINQ Transformation

Consider this transformation that a static analyzer might suggest for readability, but which is flawed:
```C#
 public IEnumerable<int> OriginalFailTakeAndSelect(int[] numbers)
 {
     int[] result = new int[10];
     int i = 0;
     while (i < 10)
     {
         result[i] = numbers[i] + 2;
         ++i;
     }
        
        return result;
 }
    
 public IEnumerable<int> TransformedFailTakeAndSelect(int[] numbers)
 {
     return numbers.Take(10).Select(n => n + 1);
 }
```
This transformation is incorrect because the projection operation differs: `n + 1` in the transformed version will not yield the same result as `n + 2` in the original.

**Output:**
```
=========================================
Message: original function does not always imply transformed function, there was at least one set of values that broke the bi-proposition.
=========================================
```
```lisp
Model:
(define-fun param!0 () (Array Int Int)
  (_ as-array k!137))
(define-fun k!136 ((x!0 Int)) Int
  (ite (= x!0 7) 10452
  (ite (= x!0 1) 21240
  (ite (= x!0 0) 7721
  (ite (= x!0 5) 8367
  (ite (= x!0 8) 30614
  (ite (= x!0 9) 2
  (ite (= x!0 4) 11799
  (ite (= x!0 3) 8857
  (ite (= x!0 2) 2439
  (ite (= x!0 6) 32287
    0)))))))))))
(define-fun k!137 ((x!0 Int)) Int
  (ite (= x!0 7) 10450
  (ite (= x!0 1) 21238
  (ite (= x!0 9) 0
  (ite (= x!0 14) 1142
  (ite (= x!0 13) (- 2)
  (ite (= x!0 5) 8365
  (ite (= x!0 8) 30612
  (ite (= x!0 3) 8855
  (ite (= x!0 0) 7719
  (ite (= x!0 4) 11797
  (ite (= x!0 2) 2437
  (ite (= x!0 6) 32285
    38)))))))))))))
(define-fun k!138 ((x!0 Int)) Int
  (ite (= x!0 7) 10451
  (ite (= x!0 1) 21239
  (ite (= x!0 9) 1
  (ite (= x!0 13) (- 1)
  (ite (= x!0 14) 1143
  (ite (= x!0 5) 8366
  (ite (= x!0 8) 30613
  (ite (= x!0 3) 8856
  (ite (= x!0 0) 7720
  (ite (= x!0 4) 11798
  (ite (= x!0 2) 2438
  (ite (= x!0 6) 32286
    39)))))))))))))
(define-fun k!135 ((x!0 Int)) Int
  (ite (= x!0 7) 10451
  (ite (= x!0 2) 2438
  (ite (= x!0 1) 21239
  (ite (= x!0 0) 7720
  (ite (= x!0 8) 30613
  (ite (= x!0 5) 8366
  (ite (= x!0 9) 1
  (ite (= x!0 4) 11798
  (ite (= x!0 3) 8856
  (ite (= x!0 6) 32286
    0)))))))))))
=========================================
SMT:
(declare-datatypes ((Enumerable_Int 0)) (((CreateEnumerable (coreEnumerable (Array Int Int))))))
(declare-fun param!0 () (Array Int Int))
(define-funs-rec ( ( BB1000 ((x!1 (Array Int Int))) Enumerable_Int)
                   ( BB1001 ((x!1 (Array Int Int))) Enumerable_Int)
                   ( Take_Int_2 ((x!1 Enumerable_Int) (x!2 Int)) Enumerable_Int)
                   ( TakeRec_Int_3
                     ((x!1 (Array Int Int))
                      (x!2 (Array Int Int))
                      (x!3 Int)
                      (x!4 Int))
                     (Array Int Int))
                   ( Select_Int_0 ((x!1 Enumerable_Int)) Enumerable_Int)
                   ( BB0000 ((x!1 (Array Int Int))) Enumerable_Int)
                   ( BB0001 ((x!1 (Array Int Int))) Enumerable_Int)
                   ( BB0002
                     ((x!1 Int) (x!2 (Array Int Int)) (x!3 (Array Int Int)))
                     Enumerable_Int)
                   ( BB0004 ((x!1 (Array Int Int))) Enumerable_Int)
                   ( BB0003
                     ((x!1 Int) (x!2 (Array Int Int)) (x!3 (Array Int Int)))
                     Enumerable_Int))
                 ( ((_ BB1001 0) x!1)
                   ((_ Take_Int_2 0)
                     ((_ Select_Int_0 0) (CreateEnumerable x!1))
                     10)
                   (CreateEnumerable ((_ TakeRec_Int_3 0)
                                       (coreEnumerable x!1)
                                       ((as const (Array Int Int)) 0)
                                       x!2
                                       0))
                   (ite (< x!4 x!3)
                        ((_ TakeRec_Int_3 0)
                          x!1
                          (store x!2 x!4 (select x!1 x!4))
                          x!3
                          (+ x!4 1))
                        x!2)
                   (CreateEnumerable ((_ map SelectCore_Int_1)
                                       (coreEnumerable x!1)))
                   ((_ BB0001 0) x!1)
                   ((_ BB0002 0) 0 x!1 ((as const (Array Int Int)) 0))
                   (ite (< x!1 10)
                        ((_ BB0003 0) x!1 x!2 x!3)
                        ((_ BB0004 0) x!3))
                   (CreateEnumerable x!1)
                   ((_ BB0002 0)
                     (+ x!1 1)
                     x!2
                     (store x!3 x!1 (+ (select x!2 x!1) 2)))))
(assert (not (= ((_ BB0000 0) param!0) ((_ BB1000 0) param!0))))
(model-add k!131 () Bool true)
(model-add k!132 () Bool true)
(model-add k!133 () Bool true)
(model-add k!134 () Bool true)
(model-add param!0 () (Array Int Int) (_ as-array k!137))
(model-add k!143
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 4)
                           11798
                           (ite (= x!1 3) 8856 (ite (= x!1 1) 21239 0)))))
             (ite (= x!1 0) 7720 (ite (= x!1 2) 2438 a!1))))
(model-add k!136
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 0)
                           7721
                           (ite (= x!1 1) 21240 (ite (= x!1 7) 10452 0)))))
           (let ((a!2 (ite (= x!1 9)
                           2
                           (ite (= x!1 8) 30614 (ite (= x!1 5) 8367 a!1)))))
           (let ((a!3 (ite (= x!1 2)
                           2439
                           (ite (= x!1 3) 8857 (ite (= x!1 4) 11799 a!2)))))
             (ite (= x!1 6) 32287 a!3)))))
(model-add k!152
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 4)
                           11799
                           (ite (= x!1 3) 8857 (ite (= x!1 1) 21240 0)))))
             (ite (= x!1 2) 2439 (ite (= x!1 0) 7721 a!1))))
(model-add k!145
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 5)
                           8366
                           (ite (= x!1 1) 21239 (ite (= x!1 0) 7720 0)))))
           (let ((a!2 (ite (= x!1 2)
                           2438
                           (ite (= x!1 4) 11798 (ite (= x!1 3) 8856 a!1)))))
             (ite (= x!1 6) 32286 a!2))))
(model-add k!138
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 9)
                           1
                           (ite (= x!1 1) 21239 (ite (= x!1 7) 10451 39)))))
           (let ((a!2 (ite (= x!1 5)
                           8366
                           (ite (= x!1 14) 1143 (ite (= x!1 13) (- 1) a!1)))))
           (let ((a!3 (ite (= x!1 0)
                           7720
                           (ite (= x!1 3) 8856 (ite (= x!1 8) 30613 a!2)))))
             (ite (= x!1 6)
                  32286
                  (ite (= x!1 2) 2438 (ite (= x!1 4) 11798 a!3)))))))
(model-add k!154
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 3)
                           8857
                           (ite (= x!1 5) 8367 (ite (= x!1 1) 21240 0)))))
           (let ((a!2 (ite (= x!1 2)
                           2439
                           (ite (= x!1 0) 7721 (ite (= x!1 4) 11799 a!1)))))
             (ite (= x!1 6) 32287 a!2))))
(model-add k!147
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 0)
                           7720
                           (ite (= x!1 1) 21239 (ite (= x!1 7) 10451 0)))))
           (let ((a!2 (ite (= x!1 3)
                           8856
                           (ite (= x!1 5) 8366 (ite (= x!1 8) 30613 a!1)))))
             (ite (= x!1 6)
                  32286
                  (ite (= x!1 2) 2438 (ite (= x!1 4) 11798 a!2))))))
(model-add array-ext
           ((x!1 (Array Int Int)) (x!2 (Array Int Int)))
           Int
           (let ((a!1 (ite (and (= x!1 (_ as-array k!137))
                                (= x!2 (_ as-array k!138)))
                           14
                           (ite (and (= x!1 (_ as-array k!138))
                                     (= x!2 (_ as-array k!135)))
                                13
                                8))))
             (ite (and (= x!1 (_ as-array k!137)) (= x!2 (_ as-array k!135)))
                  5
                  (ite (and (= x!1 (_ as-array k!138))
                            (= x!2 (_ as-array k!136)))
                       9
                       a!1))))
(model-add k!140 ((x!1 Int)) Int (ite (= x!1 0) 7720 (ite (= x!1 1) 21239 0)))
(model-add k!156
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 5)
                           8367
                           (ite (= x!1 1) 21240 (ite (= x!1 7) 10452 0)))))
           (let ((a!2 (ite (= x!1 4)
                           11799
                           (ite (= x!1 3) 8857 (ite (= x!1 8) 30614 a!1)))))
             (ite (= x!1 6) 32287 (ite (= x!1 2) 2439 (ite (= x!1 0) 7721 a!2))))))
(model-add k!149 ((x!1 Int)) Int (ite (= x!1 0) 7721 (ite (= x!1 1) 21240 0)))
(model-add k!142
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 0)
                           7720
                           (ite (= x!1 3) 8856 (ite (= x!1 1) 21239 0)))))
             (ite (= x!1 2) 2438 a!1)))
(model-add k!135
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 1)
                           21239
                           (ite (= x!1 2) 2438 (ite (= x!1 7) 10451 0)))))
           (let ((a!2 (ite (= x!1 5)
                           8366
                           (ite (= x!1 8) 30613 (ite (= x!1 0) 7720 a!1)))))
           (let ((a!3 (ite (= x!1 3)
                           8856
                           (ite (= x!1 4) 11798 (ite (= x!1 9) 1 a!2)))))
             (ite (= x!1 6) 32286 a!3)))))
(model-add k!151
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 0)
                           7721
                           (ite (= x!1 3) 8857 (ite (= x!1 1) 21240 0)))))
             (ite (= x!1 2) 2439 a!1)))
(model-add k!144
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 3)
                           8856
                           (ite (= x!1 1) 21239 (ite (= x!1 0) 7720 0)))))
             (ite (= x!1 2) 2438 (ite (= x!1 5) 8366 (ite (= x!1 4) 11798 a!1)))))
(model-add k!137
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 9)
                           0
                           (ite (= x!1 1) 21238 (ite (= x!1 7) 10450 38)))))
           (let ((a!2 (ite (= x!1 5)
                           8365
                           (ite (= x!1 13) (- 2) (ite (= x!1 14) 1142 a!1)))))
           (let ((a!3 (ite (= x!1 0)
                           7719
                           (ite (= x!1 3) 8855 (ite (= x!1 8) 30612 a!2)))))
             (ite (= x!1 6)
                  32285
                  (ite (= x!1 2) 2437 (ite (= x!1 4) 11797 a!3)))))))
(model-add k!153
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 4)
                           11799
                           (ite (= x!1 3) 8857 (ite (= x!1 1) 21240 0)))))
             (ite (= x!1 2) 2439 (ite (= x!1 5) 8367 (ite (= x!1 0) 7721 a!1)))))
(model-add k!146
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 0)
                           7720
                           (ite (= x!1 1) 21239 (ite (= x!1 7) 10451 0)))))
           (let ((a!2 (ite (= x!1 4)
                           11798
                           (ite (= x!1 3) 8856 (ite (= x!1 5) 8366 a!1)))))
             (ite (= x!1 6) 32286 (ite (= x!1 2) 2438 a!2)))))
(model-add k!139 ((x!1 Int)) Int (ite (= x!1 0) 7720 0))
(model-add k!155
           ((x!1 Int))
           Int
           (let ((a!1 (ite (= x!1 5)
                           8367
                           (ite (= x!1 1) 21240 (ite (= x!1 7) 10452 0)))))
           (let ((a!2 (ite (= x!1 0)
                           7721
                           (ite (= x!1 4) 11799 (ite (= x!1 3) 8857 a!1)))))
             (ite (= x!1 6) 32287 (ite (= x!1 2) 2439 a!2)))))
(model-add k!148 ((x!1 Int)) Int (ite (= x!1 0) 7721 0))
(model-add k!141
           ((x!1 Int))
           Int
           (ite (= x!1 2) 2438 (ite (= x!1 0) 7720 (ite (= x!1 1) 21239 0))))
(model-add k!150
           ((x!1 Int))
           Int
           (ite (= x!1 2) 2439 (ite (= x!1 0) 7721 (ite (= x!1 1) 21240 0))))
(model-add BB0004 ((x!1 (Array Int Int))) Enumerable_Int (CreateEnumerable x!1))
(model-add Take_Int_2
           ((x!1 Enumerable_Int) (x!2 Int))
           Enumerable_Int
           (CreateEnumerable ((_ TakeRec_Int_3 0)
                               (coreEnumerable x!1)
                               ((as const (Array Int Int)) 0)
                               x!2
                               0)))
(model-add BB0003
           ((x!1 Int) (x!2 (Array Int Int)) (x!3 (Array Int Int)))
           Enumerable_Int
           ((_ BB0002 0) (+ x!1 1) x!2 (store x!3 x!1 (+ (select x!2 x!1) 2))))
(model-add BB1000 ((x!1 (Array Int Int))) Enumerable_Int ((_ BB1001 0) x!1))
(model-add BB2000 ((x!1 Int)) Int ((_ BB2001 0) x!1))
(model-add Select_Int_0
           ((x!1 Enumerable_Int))
           Enumerable_Int
           (CreateEnumerable ((_ map SelectCore_Int_1) (coreEnumerable x!1))))
(model-add TakeRec_Int_3
           ((x!1 (Array Int Int)) (x!2 (Array Int Int)) (x!3 Int) (x!4 Int))
           (Array Int Int)
           (ite (< x!4 x!3)
                ((_ TakeRec_Int_3 0)
                  x!1
                  (store x!2 x!4 (select x!1 x!4))
                  x!3
                  (+ x!4 1))
                x!2))
(model-add BB1001
           ((x!1 (Array Int Int)))
           Enumerable_Int
           ((_ Take_Int_2 0) ((_ Select_Int_0 0) (CreateEnumerable x!1)) 10))
(model-add BB0001
           ((x!1 (Array Int Int)))
           Enumerable_Int
           ((_ BB0002 0) 0 x!1 ((as const (Array Int Int)) 0)))
(model-add BB0000 ((x!1 (Array Int Int))) Enumerable_Int ((_ BB0001 0) x!1))
(model-add BB0002
           ((x!1 Int) (x!2 (Array Int Int)) (x!3 (Array Int Int)))
           Enumerable_Int
           (ite (< x!1 10) ((_ BB0003 0) x!1 x!2 x!3) ((_ BB0004 0) x!3)))
(model-add BB2001 ((x!1 Int)) Int (+ x!1 1))
(model-add SelectCore_Int_1 ((x!1 Int)) Int ((_ BB2000 0) x!1))
=========================================
```
**Understanding the output:**
1.  The model provides a **counterexample**: a set of input values (see `param!0`) for which the original and transformed functions will produce different results, thus demonstrating non-equivalence.
2.  The SMT code and model can be further analyzed to understand precisely why the functions are not equivalent, using the values from the generated model.

### Example 3: Recursive vs. Iterative Factorial (Undecidable Case)

Here, we compare a classic recursive factorial implementation with an iterative one:

```C#
public int Original(int x)
{
    if (x <= 1) return 1;

    return x * Original(x - 1);
}

public int Transformed(int x)
{
    var factorial = 1;
    while (x > 1)
    {
        factorial *= x;
        --x;
    }

    return factorial;
}
```
While appearing simple, this poses a challenge for the SMT solver. This difficulty arises from the unbounded nature of recursion when attempting to prove equivalence over the entire integer domain ($-\infty$ to $+\infty$).

This scenario can lead to an **undecidable problem**, where the solver cannot definitively prove or disprove equivalence within the given constraints (time/resource limits). The solver will terminate upon reaching a timeout or the maximum configured number of operations.

**Output:**
```
=========================================
Message: undecidable problem, cannot determine if original function always imply transformed function.
=========================================
```
```lisp
SMT:
(declare-fun param!0 () Int)
(define-funs-rec ( ( BB1000 ((x!1 Int)) Int)
                   ( BB1001 ((x!1 Int)) Int)
                   ( BB1002 ((x!1 Int) (x!2 Int)) Int)
                   ( BB1004 ((x!1 Int)) Int)
                   ( BB1003 ((x!1 Int) (x!2 Int)) Int)
                   ( BB0000 ((x!1 Int)) Int)
                   ( BB0001 ((x!1 Int)) Int)
                   ( BB0004 ((x!1 Int)) Int)
                   ( BB0002 ((x!1 Int)) Int))
                 ( ((_ BB1001 0) x!1)
                   ((_ BB1002 0) 1 x!1)
                   (ite (> x!2 1) ((_ BB1003 0) x!1 x!2) ((_ BB1004 0) x!1))
                   x!1
                   ((_ BB1002 0) (* x!1 x!2) (- x!2 1))
                   ((_ BB0001 0) x!1)
                   (ite (<= x!1 1) ((_ BB0002 0) x!1) ((_ BB0004 0) x!1))
                   (* x!1 ((_ BB0000 0) (- x!1 1)))
                   1))
(assert (not (= ((_ BB0000 0) param!0) ((_ BB1000 0) param!0))))
=========================================

```

💡 Ready-to-run samples, including 13 diverse scenarios, are available in the `properties/launchSettings.json` file.

---

---
## Understanding the SMTLIB2 Output

When Dante analyzes your code, it generates **SMTLIB2** formulas that represent your C# functions mathematically. Here's how to read the key parts you'll see in the output:

### Basic Structure
```lisp
(declare-fun param!0 () MaybeString)
```
This declares a function parameter. `param!0` represents the first parameter of your original C# method.

### Data Types
```lisp
(declare-datatypes ((MaybeString 0)) (((None) (Some (value String)))))
```
This defines custom data types. Here, `MaybeString` represents C#'s nullable string (`string?`) with two cases: `None` (null) and `Some` (containing a string value) formally known as `maybe monad` .

### Function Definitions
```lisp
(define-funs-rec ( ( BB1000 ((x!1 MaybeString)) String)
                   ( BB1001 () String)
                   ...
```
These represent your C# functions as mathematical functions. `BB1000`, `BB1001`, etc. are "basic blocks" - chunks of your code without branches. The names correspond to different parts of your Control Flow Graph.

### Basic Block Bodies
```lisp
( (_ BB1001 0)
  "HelloWorld"
  ((_ BB0001 0) x!1)
  (ite ((_ is None) x!1) ((_ BB0002 0) x!1) ((_ BB0005 0) x!1))
  ((_ BB0003 0) (Some "Hello"))
  (str.++ (value x!1) "World")
  ((_ BB0003 0) (Some "Hello"))))
```
The bodies show what each basic block actually does:
- `"HelloWorld"` - A literal string value
- `(ite condition then-branch else-branch)` - Represents if-then-else logic from your C# code
- `(str.++ (value x!1) "World")` - String concatenation (`+` operator in C#)
- `((_ BB0003 0) (Some "Hello"))` - A call to another basic block with parameters

Each line corresponds to operations in your original C# code, translated into its best matching SMT theory form.

### The Core Question
```lisp
(assert (not (= ((_ BB0000 0) param!0) ((_ BB1000 0) param!0))))
```
This is the key assertion Dante asks the solver: "Find values where the original function (`BB0000`) and transformed function (`BB1000`) produce different results." If the solver finds such values, your transformation has a bug.

### Models (Counterexamples)
Consider [Example 2](#example-2-incorrect-linq-transformation) When the solver finds a counterexample, it shows a **model**:
```lisp
(define-fun param!0 () (Array Int Int)
  (_ as-array k!137))
```
This gives you concrete values that cause the functions to behave differently - essentially a test case that exposes the bug in your transformation.

The SMTLIB2 output is your C# code translated into pure mathematics, allowing the solver to exhaustively reason about all possible inputs.

## Running Dante

Dante is a command-line tool. The example command below shows typical usage.
```bash
Dante.exe -p path/to/project.csproj -c TargetClass -original OriginalMethod -transformed TransformedMethod
```
*(Note: The example uses single-hyphenated flags like `-original`. The table below lists the conceptual argument names. Ensure your command-line parser for Dante handles the form shown in the example for these specific required arguments, or adjust the example if it expects double hyphens like `--original`.)*


### Command Line Arguments

| Argument              | Short | Required | Default     | Description                                                                                                                                                                            |
|-----------------------|-------|----------|-------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| project               | -p    | Yes      |             | Path to the target `.csproj` file.                                                                                                                                                     |
| class                 | -c    | Yes      |             | Name of the non-partial target class containing the methods.                                                                                                                           |
| original              |       | Yes      |             | Name of the original method (before transformation).                                                                                                                                   |
| transformed           |       | Yes      |             | Name of the transformed method (after transformation).                                                                                                                                 |
| debug                 | -d    | No       | `false`     | If `true`, dumps the generated SMTLIB2 code and any SMT solver models.                                                                                                                 |
| highlight             |       | No       | `false`     | If `true`, applies syntax highlighting to the generated code and model output.                                                                                                         |
| recursion-depth       | -r    | No       | 1000        | The maximum depth for unfolding recursive constructs during abstract interpretation.                                                                                                   |
| undeterministic-depth | -u    | No       | `false`     | when true, the compiler will generate a random recursion depth at each evaluation point that use 'RecursionDepth' where 'RecursionDepth' is the maximum depth that can be generated.   |
| Limit                 | -l    | No       | $10^8$      | The maximum number of operations Z3 can execute.                                                                                                                                       |
| Timeout               | -t    | No       | 4294967295  | The maximum duration (in milliseconds) the solver is allowed for proving correctness or finding contradictions.                                                                        |

---
## Current Limitations

* Analysis of external or library code is generally not supported unless explicitly documented.
* LINQ support is confined to method syntax only.
* Only a subset of C# language features is currently supported.

---
## Roadmap

Future development aims to include support for:

* `foreach` loops (easy)
* Polymorphism (easy)
* Pattern matching (easy; can be facilitated for modeling polymorphic code)
* Enhanced LINQ support (easy)
* Product/Complex types and objects (medium)
* Exception handling (medium)
* `async/await` support (hard)
* Specialized support for parallel and multithreaded code (hard)
* Broader C# language feature coverage

---
## Who Is This For?

Dante is primarily aimed at:

* Developers of static analysis tools.
* Developers creating code transformation and refactoring tools.
* Developers utilizing LLMs for code transformation and refactoring tasks.
* IDE developers interested in formally verifying code transformation and analysis passes.

---
## Documentation

https://yazandaba.hashnode.dev/dante-a-case-study-in-compiler-assisted-verification-using-model-checking

---
## License

This project is licensed under the MIT License. See the `LICENSE` file for more details.