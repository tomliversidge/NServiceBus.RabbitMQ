﻿namespace NServiceBus.Features
{
    using System;
    using EasyNetQ;
    using Support;
    using Transports;
    using Transports.RabbitMQ;
    using Transports.RabbitMQ.Config;
    using Transports.RabbitMQ.Routing;

    class RabbitMqTransport : ConfigureTransport
    {
        protected override void Configure(FeatureConfigurationContext context, string connectionString)
        {
            var queueName = context.Settings.EndpointName();

            if (!context.Settings.GetOrDefault<bool>("ScaleOut.UseSingleBrokerQueue"))
            {
                queueName += string.Format(".{0}", RuntimeEnvironment.MachineName);
                LocalAddress(queueName);
            }

            var connectionConfiguration = new ConnectionStringParser(context.Settings).Parse(connectionString);

            context.Container.RegisterSingleton(connectionConfiguration);

            context.Container.ConfigureComponent<RabbitMqDequeueStrategy>(DependencyLifecycle.InstancePerCall)
                 .ConfigureProperty(p => p.PrefetchCount, connectionConfiguration.PrefetchCount);

            context.Container.ConfigureComponent<OpenPublishChannelBehavior>(DependencyLifecycle.InstancePerCall);

            context.Pipeline.Register<OpenPublishChannelBehavior.Registration>();

            context.Container.ConfigureComponent<ChannelProvider>(DependencyLifecycle.InstancePerCall)
                  .ConfigureProperty(p => p.UsePublisherConfirms, connectionConfiguration.UsePublisherConfirms)
                  .ConfigureProperty(p => p.MaxWaitTimeForConfirms, connectionConfiguration.MaxWaitTimeForConfirms);

            context.Container.ConfigureComponent<RabbitMqDequeueStrategy>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<RabbitMqMessageSender>(DependencyLifecycle.InstancePerCall);
            context.Container.ConfigureComponent<RabbitMqMessagePublisher>(DependencyLifecycle.InstancePerCall);

            context.Container.ConfigureComponent<RabbitMqSubscriptionManager>(DependencyLifecycle.SingleInstance)
             .ConfigureProperty(p => p.EndpointQueueName, queueName);

            context.Container.ConfigureComponent<RabbitMqQueueCreator>(DependencyLifecycle.InstancePerCall)
                .ConfigureProperty(t => t.UseDurableQueues, context.Settings.Get<bool>("Endpoint.DurableMessages"));


            if (context.Settings.HasSetting<IRoutingTopology>())
            {
                context.Container.RegisterSingleton(context.Settings.Get<IRoutingTopology>());
            }
            else
            {
                context.Container.ConfigureComponent<ConventionalRoutingTopology>(DependencyLifecycle.SingleInstance);
            }

            if (context.Settings.HasSetting("IManageRabbitMqConnections"))
            {
                context.Container.ConfigureComponent(context.Settings.Get<Type>("IManageRabbitMqConnections"), DependencyLifecycle.SingleInstance);
            }
            else
            {
                context.Container.ConfigureComponent<RabbitMqConnectionManager>(DependencyLifecycle.SingleInstance);

                context.Container.ConfigureComponent<IConnectionFactory>(builder => new ConnectionFactoryWrapper(builder.Build<IConnectionConfiguration>(), new DefaultClusterHostSelectionStrategy<ConnectionFactoryInfo>()), DependencyLifecycle.InstancePerCall);
            }
        }

        protected override string ExampleConnectionStringForErrorMessage
        {
            get { return "host=localhost"; }
        }
    }
}