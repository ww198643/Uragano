﻿using System.Threading.Tasks;
using Sample.Common;
using Uragano.Abstractions;

namespace Sample.Service.Interfaces
{
    //[ClientInterceptor_1_]
    //[ClientInterceptor_2_]
    [ServiceDiscoveryName("RPC")]
    [ServiceRoute("hello")]
    public interface IHelloService : IService
    {
        //[ClientMethodInterceptor_1_]
        //[ClientMethodInterceptor_2_]
        [CircuitBreaker(FallbackExecuteScript = "return new ResultModel{Message=\"fallback\"};", ScriptUsingNameSpaces = new[] { "Sample.Service.Interfaces" })]
        [Caching(Key = "customKey:{0}")]
        [ServiceRoute("say/async")]
        Task<ResultModel> SayHello(string name);

        [ServiceRoute("say/async/entity")]
        Task<ResultModel> SayHello(TestModel testModel);

        [ServiceRoute("say/task")]
        Task SayHello();

        [ServiceRoute("say/int")]
        Task<int> Age();
    }
}
