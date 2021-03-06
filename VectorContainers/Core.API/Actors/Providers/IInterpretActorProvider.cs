﻿using System.Threading.Tasks;
using Core.API.Messages;

namespace Core.API.Actors.Providers
{
    public interface IInterpretActorProvider<TModel>
    {
        Task<bool> Interpret(InterpretMessage<TModel> message);
    }
}
