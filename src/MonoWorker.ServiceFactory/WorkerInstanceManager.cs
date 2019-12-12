﻿using BlazorWorker.BackgroundServiceFactory;
using BlazorWorker.BackgroundServiceFactory.Shared;
using BlazorWorker.Core;
using MonoWorker.Core;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace MonoWorker.BackgroundServiceHost
{
    public class WorkerInstanceManager : IWorkerMessageService
    {
        public interface IEventWrapper {
            long InstanceId { get; }
            long EventId { get; }
        }
        public readonly Dictionary<long, object> instances = new Dictionary<long, object>();
        public readonly Dictionary<long, Dictionary<long, IEventWrapper>> events =
             new Dictionary<long, Dictionary<long, IEventWrapper>>();

        public static readonly WorkerInstanceManager Instance = new WorkerInstanceManager();
        private readonly ISerializer serializer;
        private readonly WebWorkerOptions options;

        public event EventHandler<string> IncomingMessage;

        public WorkerInstanceManager()
        {
            this.serializer = new DefaultMessageSerializer();
            this.options = new WebWorkerOptions();
        }

        public static void Init() {
            MessageService.Message += Instance.OnMessage;
            Console.WriteLine("MonoWorker.BackgroundServiceHost.Init(): Done.");
            Instance.PostObject(new InitWorkerComplete());
        }

        public async Task PostMessageAsync(string message)
        {
            PostMessage(message);
        }

        public void PostMessage(string message)
        {
            Console.WriteLine($"MonoWorker.BackgroundServiceHost.PostMessage(): {message}.");
            MessageService.PostMessage(message);
        }

        private async Task PostObjecAsync<T>(T obj)
        {
            PostMessage(this.serializer.Serialize(obj));
        }

        private void PostObject<T>(T obj)
        {
            PostMessage(this.serializer.Serialize(obj));
        }

        private void OnMessage(object sender, string message)
        {
            var baseMessage = this.serializer.Deserialize<BaseMessage>(message);
            if (baseMessage.MessageType == nameof(InitInstanceParams))
            {
                var initMessage = this.serializer.Deserialize<InitInstanceParams>(message);
                InitInstance(initMessage);
                return;
            }

            if (baseMessage.MessageType == nameof(MethodCallParams))
            {
                var methodCallMessage = this.serializer.Deserialize<MethodCallParams>(message);
                try
                {
                    var result = Call(methodCallMessage);
                    PostObject(
                        
                        new MethodCallResult()
                        {
                            CallId = methodCallMessage.CallId,
                            ResultPayload = this.serializer.Serialize(result)
                        }
                    );
                }
                catch (Exception e)
                {
                    PostObject(
                        new MethodCallResult()
                        {
                            CallId = methodCallMessage.CallId,
                            IsException = true,
                            Exception = e
                        });
                }
                return;
            }

            if (baseMessage.MessageType == nameof(RegisterEvent))
            {
                var registerEventMessage = this.serializer.Deserialize<RegisterEvent>(message);
                RegisterEvent(registerEventMessage);
            }

            IncomingMessage?.Invoke(this, message);
        }

        private void RegisterEvent(RegisterEvent registerEventMessage)
        {
            var instance = instances[registerEventMessage.InstanceId];
            var eventSignature = instance.GetType().GetEvent(registerEventMessage.EventName);

            // TODO: This can be cached.
            var wrapperType = typeof(EventHandlerWrapper<>)
                .MakeGenericType(Type.GetType(registerEventMessage.EventHandlerTypeArg));
            
            var wrapper = (IEventWrapper)Activator.CreateInstance(wrapperType, this, registerEventMessage.InstanceId, registerEventMessage.EventHandleId);
            var delegateMethod = Delegate.CreateDelegate(eventSignature.EventHandlerType, wrapper, "OnEvent"); // this.GetType().GetMethod(nameof(EventHandlerWrapper<object>.OnEvent)));
            eventSignature.AddEventHandler(instance, delegateMethod);
            if (!events.TryGetValue(wrapper.InstanceId, out var wrappers))
            {
                wrappers = new Dictionary<long, IEventWrapper>();
                events.Add(wrapper.InstanceId, wrappers);
            }
            wrappers.Add(wrapper.EventId, wrapper);
        }

        public class EventHandlerWrapper<T> : IEventWrapper
        {
            private readonly WorkerInstanceManager wim;

            public EventHandlerWrapper(WorkerInstanceManager wim, long instanceId, long eventId)
            {
                this.wim = wim;
                InstanceId = instanceId;
                EventId = eventId;
            }

            public long InstanceId { get; }
            public long EventId { get; }

            public void OnEvent(object _, T eventArgs)
            {
                Console.WriteLine("ONEVENT");
                wim.PostObject(new EventRaised() { 
                    EventHandleId = EventId,
                    InstanceId = InstanceId,
                    ResultPayload = wim.serializer.Serialize(eventArgs)
                });
            }
        }

        public void InitInstance(InitInstanceParams createInstanceInfo)
        {
            Type type;
            try
            {
                type = Type.GetType($"{createInstanceInfo.TypeName}, {createInstanceInfo.AssemblyName}");
            }
            catch (Exception e)
            {
                throw new InitWorkerInstanceException($"Unable to to load type {createInstanceInfo.TypeName} from {createInstanceInfo.AssemblyName}", e);
            }

            //TODO: inject message service here if applicable
            try
            {
                instances[createInstanceInfo.InstanceId] = Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                throw new InitWorkerInstanceException($"Unable to to instanciate type {createInstanceInfo.TypeName} from {createInstanceInfo.AssemblyName}", e);
            }

            Instance.PostObject(
                new InitInstanceComplete() { 
                    CallId = createInstanceInfo.CallId 
                });
        }

        public object Call(MethodCallParams instanceMethodCallParams)
        {
            var instance = instances[instanceMethodCallParams.InstanceId];
            var lambda = this.options.ExpressionSerializer.Deserialize(instanceMethodCallParams.SerializedExpression) 
                as LambdaExpression;
            var dynamicDelegate = lambda.Compile();
            return dynamicDelegate.DynamicInvoke(instance);
        }
    }
}