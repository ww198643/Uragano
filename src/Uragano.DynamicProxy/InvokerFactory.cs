﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Uragano.Abstractions.Exceptions;
using Uragano.Abstractions.ServiceInvoker;
using Microsoft.Extensions.DependencyInjection;
using Uragano.DynamicProxy.Interceptor;
using ServiceDescriptor = Uragano.Abstractions.ServiceDescriptor;

namespace Uragano.DynamicProxy
{
	public class InvokerFactory : IInvokerFactory
	{
		private static readonly ConcurrentDictionary<string, ServiceDescriptor>
			ServiceInvokers = new ConcurrentDictionary<string, ServiceDescriptor>();

		private static readonly ConcurrentDictionary<MethodInfo, string>
			MethodMapRoute = new ConcurrentDictionary<MethodInfo, string>();

		private IServiceProvider ServiceProvider { get; }
		public InvokerFactory(IServiceProvider serviceProvider)
		{
			ServiceProvider = serviceProvider;
		}

		public void Create(string route, Type interfaceType, MethodInfo methodInfo, List<Type> interceptorTypes)
		{
			route = route.ToLower();
			if (ServiceInvokers.ContainsKey(route))
				throw new Exception();
			ServiceInvokers.TryAdd(route, new ServiceDescriptor
			{
				Route = route,
				InterfaceType = interfaceType,
				MethodInfo = methodInfo,
				MethodInvoker = new MethodInvoker(methodInfo),
				Interceptors = interceptorTypes
			});
			MethodMapRoute.TryAdd(methodInfo, route.ToLower());
		}

		public ServiceDescriptor Get(string route)
		{
			if (ServiceInvokers.TryGetValue(route.ToLower(), out var value))
				return value;
			throw new NotFoundRouteException(route);
		}

		public ServiceDescriptor Get(MethodInfo methodInfo)
		{
			if (!MethodMapRoute.TryGetValue(methodInfo, out var route)) throw new NotFoundRouteException("");
			if (ServiceInvokers.TryGetValue(route, out var serviceDescriptor))
			{
				return serviceDescriptor;
			}
			throw new NotFoundRouteException(route);
		}

		public async Task<object> Invoke(string route, object[] args, Dictionary<string, string> meta)
		{
			if (!ServiceInvokers.TryGetValue(route, out var service))
				throw new Exception();
			using (var scope = ServiceProvider.CreateScope())
			{
				var context = new InterceptorContext
				{
					ServiceRoute = service.Route,
					ServiceProvider = scope.ServiceProvider,
					Args = args,
					Meta = meta
				};
				context.Interceptors.Push(typeof(ServerDefaultInterceptor));
				for (var i = 0; i < service.Interceptors.Count - 1; i++)
				{
					context.Interceptors.Push(service.Interceptors[i]);
				}
				var instance = scope.ServiceProvider.GetRequiredService(service.InterfaceType);
				return await Task.FromResult(service.MethodInvoker.Invoke(instance, args));
			}
		}
	}
}
