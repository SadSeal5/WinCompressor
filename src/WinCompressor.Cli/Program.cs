using WinCompressor.Core.Cli;

var exitCode = await CliDispatcher.CreateDefault().RunAsync(args);
return exitCode;
