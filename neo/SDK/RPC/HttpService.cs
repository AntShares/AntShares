﻿using Neo.IO.Json;
using Neo.SDK.RPC.Model;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Neo.SDK.RPC
{
    public class HttpService : IRpcService
    {
        private readonly HttpClient httpClient;

        public HttpService(string url)
        {
            httpClient = new HttpClient() { BaseAddress = new Uri(url) };
        }

        public HttpService(HttpClient client)
        {
            httpClient = client;
        }

        public async Task<JObject> SendAsync(object request)
        {
            var requestJson = JsonConvert.SerializeObject(request);
            var result = await httpClient.PostAsync(httpClient.BaseAddress, new StringContent(requestJson, Encoding.UTF8));
            var content = await result.Content.ReadAsStringAsync();
            var response = RPCResponse.FromJson(JObject.Parse(content));

            if (response.Error != null)
            {
                throw new NeoSdkException(response.Error.Code, response.Error.Message, response.Error.Data);
            }

            return response.Result;
        }

        public JObject Send(object request)
        {
            try
            {
                return SendAsync(request).Result;
            }
            catch (AggregateException ex)
            {
                throw ex.GetBaseException();
            }
        }

    }


}
