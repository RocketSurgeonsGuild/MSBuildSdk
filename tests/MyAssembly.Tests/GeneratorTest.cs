using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.Extensions.Testing;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Rocket.Surgery.MyAssembly.Tests;

public abstract class GeneratorTest() : LoggerTest<TestRecord>(new())
{
    public GeneratorTestContextBuilder Builder { get; } = GeneratorTestContextBuilder
                                                         .Create()
                                                         .WithGenerator<ConstantsGenerator>()
                                                         .WithGenerator<ResourcesGenerator>()
                                                         .WithGenerator<StringsGenerator>()
                                                         .AddReferences(
                                                              typeof(ActivatorUtilities).Assembly,
                                                              typeof(IServiceProvider).Assembly,
                                                              typeof(System.Collections.Concurrent.ConcurrentDictionary<,>).Assembly
                                                          );
}
