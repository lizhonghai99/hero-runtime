using System.Runtime.CompilerServices;

// Cli project sets <AssemblyName>hi</AssemblyName>.
[assembly: InternalsVisibleTo("hi")]
[assembly: InternalsVisibleTo("Hero.Cli")]
[assembly: InternalsVisibleTo("Hero.Core.Tests")]
