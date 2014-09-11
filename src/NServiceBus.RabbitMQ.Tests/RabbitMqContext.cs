﻿namespace NServiceBus.Transports.RabbitMQ.Tests
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Transactions;
    using Config;
    using EasyNetQ;
    using global::RabbitMQ.Client;
    using NServiceBus.Support;
    using NUnit.Framework;
    using ObjectBuilder;
    using ObjectBuilder.Common;
    using Pipeline;
    using Routing;
    using Settings;
    using TransactionSettings = Unicast.Transport.TransactionSettings;

    class RabbitMqContext
    {
        protected void MakeSureQueueAndExchangeExists(string queueName)
        {
            using (var channel = connectionManager.GetAdministrationConnection().CreateModel())
            {
                //create main q
                channel.QueueDeclare(queueName, true, false, false, null);
                channel.QueuePurge(queueName);

                //create callback q
                channel.QueueDeclare(CallbackQueue, true, false, false, null);
                channel.QueuePurge(CallbackQueue);

                //to make sure we kill old subscriptions
                DeleteExchange(queueName);

                routingTopology.Initialize(channel, queueName);
            }
        }

        void DeleteExchange(string exchangeName)
        {
            var connection = connectionManager.GetAdministrationConnection();
            using (var channel = connection.CreateModel())
            {
                try
                {
                    channel.ExchangeDelete(exchangeName);
                }
                    // ReSharper disable EmptyGeneralCatchClause
                catch (Exception)
                    // ReSharper restore EmptyGeneralCatchClause
                {
                }
            }
        }

        public virtual int MaximumConcurrency
        {
            get { return 1; }
        }
       
        [SetUp]
        public void SetUp()
        {
            routingTopology = new ConventionalRoutingTopology();
            receivedMessages = new BlockingCollection<TransportMessage>();

            var config = new ConnectionConfiguration();
            config.ParseHosts("localhost:5672");

            var selectionStrategy = new DefaultClusterHostSelectionStrategy<ConnectionFactoryInfo>();
            var connectionFactory = new ConnectionFactoryWrapper(config, selectionStrategy);
            connectionManager = new RabbitMqConnectionManager(connectionFactory, config);

            publishChannel = connectionManager.GetPublishConnection().CreateModel();

            var channelProvider = new FakeChannelProvider(publishChannel);

            sender = new RabbitMqMessageSender
            {
                ChannelProvider = channelProvider,
                RoutingTopology = routingTopology,
                CallbackQueue = CallbackQueue
            };

            dequeueStrategy = new RabbitMqDequeueStrategy(connectionManager, null,
                new Configure(new SettingsHolder(), new FakeContainer(), new List<Action<IConfigureComponents>>(), new PipelineSettings(new BusConfiguration())),
                new SecondaryReceiveConfiguration(s =>
                {
                    var settings = new SecondaryReceiveSettings
                    {
                        MaximumConcurrencyLevel = 1
                    };

                    settings.SecondaryQueues.Add(CallbackQueue);

                    return settings;
                }));
            

            MakeSureQueueAndExchangeExists(ReceiverQueue);


            MessagePublisher = new RabbitMqMessagePublisher
            {
                ChannelProvider = channelProvider,
                RoutingTopology = routingTopology
            };
            subscriptionManager = new RabbitMqSubscriptionManager
            {
                ConnectionManager = connectionManager,
                EndpointQueueName = ReceiverQueue,
                RoutingTopology = routingTopology
            };

            dequeueStrategy.Init(Address.Parse(ReceiverQueue), new TransactionSettings(true, TimeSpan.FromSeconds(30), IsolationLevel.ReadCommitted, 5, false, false), m =>
            {
                receivedMessages.Add(m);
                return true;
            }, (s, exception) => { });

            dequeueStrategy.Start(MaximumConcurrency);
        }


        [TearDown]
        public void TearDown()
        {
            if (dequeueStrategy != null)
            {
                dequeueStrategy.Stop();
            }

            publishChannel.Close();
            publishChannel.Dispose();

            connectionManager.Dispose();
        }

        protected virtual string ExchangeNameConvention()
        {
            return "amq.topic";
        }


        protected TransportMessage WaitForMessage()
        {
            var waitTime = TimeSpan.FromSeconds(1);

            if (Debugger.IsAttached)
            {
                waitTime = TimeSpan.FromMinutes(10);
            }

            TransportMessage message;
            receivedMessages.TryTake(out message, waitTime);

            return message;
        }

        IModel publishChannel;
        protected string CallbackQueue = "testreceiver." + RuntimeEnvironment.MachineName;

        protected const string ReceiverQueue = "testreceiver";
        protected RabbitMqMessagePublisher MessagePublisher;
        protected RabbitMqConnectionManager connectionManager;
        protected RabbitMqDequeueStrategy dequeueStrategy;
        BlockingCollection<TransportMessage> receivedMessages;

        protected ConventionalRoutingTopology routingTopology;
        protected RabbitMqMessageSender sender;
        protected RabbitMqSubscriptionManager subscriptionManager;
    }

    class FakeContainer : IContainer
    {
        public void Dispose()
        {
        }

        public object Build(Type typeToBuild)
        {
            throw new NotImplementedException();
        }

        public IContainer BuildChildContainer()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<object> BuildAll(Type typeToBuild)
        {
            throw new NotImplementedException();
        }

        public void Configure(Type component, DependencyLifecycle dependencyLifecycle)
        {
            
        }

        public void Configure<T>(Func<T> component, DependencyLifecycle dependencyLifecycle)
        {
            
        }

        public void ConfigureProperty(Type component, string property, object value)
        {
            
        }

        public void RegisterSingleton(Type lookupType, object instance)
        {
            
        }

        public bool HasComponent(Type componentType)
        {
            throw new NotImplementedException();
        }

        public void Release(object instance)
        {
            
        }
    }

    class FakeChannelProvider:IChannelProvider
    {
        readonly IModel publishChannel;

        public FakeChannelProvider(IModel publishChannel)
        {
            this.publishChannel = publishChannel;
        }

        public bool TryGetPublishChannel(out IModel channel)
        {
            channel = publishChannel;

            return true;
        }

        public ConfirmsAwareChannel GetNewPublishChannel()
        {
            throw new NotImplementedException();
        }
    }
}