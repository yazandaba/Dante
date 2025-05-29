using System.ComponentModel;
using Ookii.CommandLine;

namespace Dante;

[ApplicationFriendlyName("Dante")]
[Description("Fomral verification tool based on core Dante compiler")]
[Serializable]
[GeneratedParser]
internal partial class DanteCommandLine
{
    [CommandLineArgument("project", ShortName = 'p', IsRequired = true)]
    [Description("path to targeted csproj file.")]
    public string Project { get; set; } = string.Empty;

    [CommandLineArgument("class", ShortName = 'c', IsRequired = true)]
    [Description("name of non partial targeted class.")]
    public string Class { get; set; } = string.Empty;

    [CommandLineArgument("original", IsRequired = true)]
    [Description("name of original method before any code transformation.")]
    public string Original { get; set; } = string.Empty;

    [CommandLineArgument("transformed", IsRequired = true)]
    [Description("name of transformed method after code transformation.")]
    public string Transformed { get; set; } = string.Empty;

    [CommandLineArgument("debug", ShortName = 'd', DefaultValue = false, IsRequired = false)]
    [Description("if true SMTLIB2 generated code and optional SMT solver models will get dumped.")]
    public bool Debug { get; set; }

    [CommandLineArgument("highlight", DefaultValue = false, IsRequired = false)]
    [Description("syntax highlight generated code and model.")]
    public bool Pretty { get; set; }

    [CommandLineArgument("recursion-depth", ShortName = 'r', DefaultValue = 1000, IsRequired = false)]
    [Description("the maximum number of recursive constructs depth when abstractlly intepreteated.")]
    public uint RecursionDepth { get; set; }

    [CommandLineArgument("undeterministic-depth", ShortName = 'u', DefaultValue = false, IsRequired = false)]
    [Description("when true, the compiler will generate a random recursion depth at each evaluation point" +
                 "that use 'RecursionDepth' where 'RecursionDepth' is the maximum depth that can be generated.")]
    public bool RandomDepth { get; set; }

    [CommandLineArgument("limit", ShortName = 'l', DefaultValue = 1_000_000_00, IsRequired = false)]
    [Description("the maximum number of operations that can be executed by Z3.")]
    public uint Limit { get; set; }

    [CommandLineArgument("timeout", ShortName = 't', DefaultValue = uint.MaxValue, IsRequired = false)]
    [Description(
        "The maximum duration (in milliseconds) that the solver is allowed to spend attempting to either prove " +
        "the correctness of a logical formula or find contradictions within it.")]
    public uint Timeout { get; set; }
}