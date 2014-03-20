using System;
using System.Dynamic;
using System.Collections.Generic;
using AutoTest.Messages;
using AutoTest.Core.Caching;
using AutoTest.Core.Messaging;
using AutoTest.Core.Caching.Projects;

namespace AutoTest.Server.Handlers
{
	class TriggerRunHandler : IHandler, IClientHandler
	{
        private Action<string, object> _dispatcher;
        private ICache _cache;
        private IMessageBus _bus;

        public TriggerRunHandler(ICache cache, IMessageBus bus) {
            _cache = cache;
            _bus = bus;
        }

        public void DispatchThrough(Action<string, object> dispatcher) {
            _dispatcher = dispatcher;
        }

        public Dictionary<string, Action<dynamic>> GetClientHandlers() {
            var handlers = new Dictionary<string, Action<dynamic>>();
            handlers.Add("build-test-all", (msg) => {
                var message = new ProjectChangeMessage();
                var projects = _cache.GetAll<Project>();
                foreach (var project in projects) {
                    if (project.Value == null)
                        continue;
                    project.Value.RebuildOnNextRun();
                    message.AddFile(new ChangedFile(project.Key));
                }
                _bus.Publish(message);
            });

            return handlers;
        }
	}
}