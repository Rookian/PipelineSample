// ReSharper disable UnusedMember.Global
#pragma warning disable CA1822
namespace PipelineSample
{
    internal class Program
    {
        static void Main()
        {
            Console.WriteLine("Hello, World!");

            var pipeline = new Pipeline();
            pipeline.Add<Pipe>();
            pipeline.Add<Pipe1>();
            pipeline.Add<Pipe2>();
            pipeline.OnStepExecuted<Pipe1Result>(x => Console.WriteLine(x.Message));
            pipeline.Execute();
        }
    }

    public class Pipe
    {
        public void Execute() => Console.WriteLine("Empty");
    }

    public record Pipe1Result(string Message);
    public class Pipe1
    {
        public Pipe1Result Execute() => new("Pipe1");
    }

    public record Pipe2Result(string Message);
    public class Pipe2
    {
        private readonly Pipe1Result _pipe1Result;

        public Pipe2(Pipe1Result pipe1Result)
        {
            _pipe1Result = pipe1Result;
        }

        public Pipe2Result Execute() => new($"Pipe1 + {_pipe1Result.Message}");
    }

    public class Pipeline
    {
        private readonly List<Type> _steps = new();
        private readonly Dictionary<Type, object> _cachedResults = new();
        private readonly Dictionary<Type, Delegate> _observers = new();

        public Pipeline Add<T>()
        {
            _steps.Add(typeof(T));
            return this;
        }

        public void Execute()
        {
            foreach (var stepType in _steps)
            {
                var step = CreateStepInstance(stepType);
                ExecuteStep(step);
            }
        }

        private object CreateStepInstance(Type stepType)
        {
            // If the step has dependencies, use cached results to resolve them
            var constructor = stepType.GetConstructors()[0];
            var parameters = constructor.GetParameters();
            var constructorArguments = new List<object>();

            foreach (var parameter in parameters)
            {
                if (_cachedResults.TryGetValue(parameter.ParameterType, out var cachedResult))
                {
                    constructorArguments.Add(cachedResult);
                }
                else
                {
                    var wrongStep = DeterminePipelineStepForOutputType(parameter.ParameterType);
                    if (wrongStep == null)
                    {
                        throw new InvalidOperationException($"Dependency {parameter.ParameterType} for {stepType.Name} was not found for any registered step.");
                    }

                    throw new InvalidOperationException($"Please register step {wrongStep.Name} before step {stepType.Name}");
                }
            }

            return Activator.CreateInstance(stepType, constructorArguments.ToArray())!;
        }

        private Type? DeterminePipelineStepForOutputType(Type outputType) => _steps
            .SingleOrDefault(step => step.GetMethods().SingleOrDefault(x => x.ReturnType.IsAssignableFrom(outputType)) != null);

        private void ExecuteStep(object step)
        {
            var method = step.GetType().GetMethod("Execute") ?? throw new InvalidOperationException($"Step {step.GetType().Name} has no Execute method defined.");
            var result = method.Invoke(step, null);

            if (result == null) return;

            if (_observers.TryGetValue(result.GetType(), out var action))
            {
                action.DynamicInvoke(result);
            }
            _cachedResults[result.GetType()] = result;
        }

        public void OnStepExecuted<T>(Action<T> action)
        {
            _observers.Add(typeof(T), action);
        }
    }
}
