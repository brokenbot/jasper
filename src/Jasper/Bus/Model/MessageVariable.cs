﻿using System;
using BlueMilk.Codegen;
using BlueMilk.Codegen.Variables;

namespace Jasper.Bus.Model
{
    public class MessageVariable : Variable
    {
        public MessageVariable(Variable envelope, Type messageType) : base(messageType, DefaultArgName(messageType))
        {
            Creator = new MessageFrame(this, envelope);
        }
    }
}
