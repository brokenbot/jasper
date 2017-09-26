﻿using BlueMilk.Codegen;
using BlueMilk.Compilation;

namespace Jasper.Util.StructureMap
{
    public class NestedContainerCreation : Frame
    {
        public NestedContainerCreation(Variable root) : base(false)
        {
            uses.Add(root);

            Wraps = true;
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.UsingBlock("var nested = _root.GetNestedContainer()", w => Next?.GenerateCode(method, writer));
        }
    }
}