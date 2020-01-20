﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.Features.RPC.ModelBinders
{
    public class MoneyModelBinder : IModelBinder, IModelBinderProvider
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext.ModelType != typeof(Money)) return Task.CompletedTask;

            var val = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

            var key = val.FirstValue;
            if (key == null) return Task.CompletedTask;
            return Task.FromResult(Money.Parse(key));
        }

        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.ModelType == typeof(Money))
                return this;
            return null;
        }
    }
}