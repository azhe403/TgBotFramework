using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TgBotFramework.UpdateProcessing
{
    public class SingleThreadProcessor<TBot, TContext> : BackgroundService  
        where TBot : BaseBot
        where TContext : IUpdateContext 
    {
        private readonly ILogger<SingleThreadProcessor<TBot, TContext>> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TBot _bot;
        private readonly BotFramework<TContext> _framework;
        private readonly ChannelReader<IUpdateContext> _updatesQueue;

        public SingleThreadProcessor(ILogger<SingleThreadProcessor<TBot, TContext>> logger,
            IServiceProvider serviceProvider,
            Channel<IUpdateContext> updatesQueue, 
            TBot bot, 
            BotFramework<TContext> framework
            ) 
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _bot = bot;
            _updatesQueue = updatesQueue.Reader;
            
            //check pipeline
            _framework = framework;
            _framework.Check(serviceProvider);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            _logger.LogInformation("SingleThreadProcessor starts working");
            await foreach (var update in _updatesQueue.ReadAllAsync(stoppingToken))
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("SingleThreadProcessor stops work");
                    return;
                }
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    update.Services = scope.ServiceProvider;
                    update.Client = _bot.Client;
                    update.Bot = _bot;
                    await _framework.Execute((TContext) update, stoppingToken);
                    if (update.Result != null)
                    {
                        Task.Run(() => update.Result.TrySetResult(), stoppingToken);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e, "Oops");
                }
            }
        }
    }
}