#:package Microsoft.VisualStudio.SolutionPersistence
#:package Sourcy.Git
#:package Sourcy.DotNet
#:package Rocket.Surgery.ModularPipelines.Extensions
#:property ImportConventions=true
#:property JsonSerializerIsReflectionEnabledByDefault=true

using Build;
using ModularPipelines;
using ModularPipelines.Modules;
using ModularPipelines.Plugins;
using Rocket.Surgery.ModularPipelines.Extensions;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

var pipelineBuilder = Pipeline.CreateBuilder(args);
PluginRegistry.Register(new ConventionsPlugin(ConventionContextBuilder.Create(Imports.Instance)
.AddIfMissing(nameof(MyAssembly.Project.BuildScriptsRoot), MyAssembly.Project.BuildScriptsRoot),
a => !( a == typeof(RemoveUnusedDependenciesModule) )));
await pipelineBuilder.Build().RunAsync();
