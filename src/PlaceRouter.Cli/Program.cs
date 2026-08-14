using PlaceRouter.Cli;
using PlaceRouter.Infrastructure.Composition;

var service = PlaceRouterComposition.CreateProjectService();
var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0";
return new CliApplication(service, Console.Out, Console.Error, version).Run(args);
