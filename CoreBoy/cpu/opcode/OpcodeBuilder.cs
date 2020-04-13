//namespace eu.rekawek.coffeegb.cpu.opcode 

using System;
using System.Collections.Generic;
using CoreBoy.cpu.op;
using CoreBoy.gpu;
using static CoreBoy.cpu.BitUtils;

using IntRegistryFunction = System.Func<CoreBoy.cpu.Flags, int, int>;
using BiIntRegistryFunction = System.Func<CoreBoy.cpu.Flags, int, int, int>;

namespace CoreBoy.cpu.opcode
{
    public class OpcodeBuilder
    {
        private static readonly AluFunctions ALU;
        public static readonly List<IntRegistryFunction> OEM_BUG;

        static OpcodeBuilder()
        {
            ALU = new AluFunctions();
            OEM_BUG = new List<IntRegistryFunction>
            {
                ALU.findAluFunction("INC", DataType.D16),
                ALU.findAluFunction("DEC", DataType.D16)
            };
        }

        private readonly int opcode;
        private readonly string label;
        private readonly List<Op> ops = new List<Op>();
        private DataType lastDataType;

        public OpcodeBuilder(int opcode, string label) 
        {
            this.opcode = opcode;
            this.label = label;
        }

        public OpcodeBuilder copyByte(string target, string source) {
            load(source);
            store(target);
            return this;
        }

        private class LoadOp : Op
        {
            private readonly Argument _arg;
            public LoadOp(Argument arg) => _arg = arg;
            public override bool readsMemory() => _arg.isMemory();
            public override int operandLength() => _arg.getOperandLength();
            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context) => _arg.read(registers, addressSpace, args);
            public override string ToString() => string.Format(_arg.getDataType() == DataType.D16 ? "%s → [__]" : "%s → [_]", _arg.getLabel());
        }

        public OpcodeBuilder load(string source) {
            var arg = Argument.parse(source);
            lastDataType = arg.getDataType();
            ops.Add(new LoadOp(arg));
            return this;
        }


        private class LoadWordOp : Op
        {
            private readonly int _value;
            public LoadWordOp(int value) => _value = value;
            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context) => _value;
            public override string ToString() => string.Format("0x%02X → [__]", _value);
        }

        public OpcodeBuilder loadWord(int value) {
            lastDataType = DataType.D16;
            ops.Add(new LoadWordOp(value));
            return this;
        }

        private class Store_a16_Op1 : Op
        {
            private readonly Argument _arg;
            public Store_a16_Op1(Argument arg) => _arg = arg;
            public override bool writesMemory() => _arg.isMemory();
            public override int operandLength() => _arg.getOperandLength();
            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context)
            {
                addressSpace.setByte(toWord(args), context & 0x00ff);
                return context;
            }
            public override string ToString() => string.Format("[ _] → %s", _arg.getLabel());
        }

        private class Store_a16_Op2 : Op
        {
            private readonly Argument _arg;
            public Store_a16_Op2(Argument arg) => _arg = arg;
            public override bool writesMemory() => _arg.isMemory();
            public override int operandLength() => _arg.getOperandLength();
            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context)
            {
                addressSpace.setByte((toWord(args) + 1) & 0xffff, (context & 0xff00) >> 8);
                return context;
            }
            public override string ToString() => string.Format("[_ ] → %s", _arg.getLabel());
        }

        private class Store_LastDataType : Op
        {
            private readonly Argument _arg;
            public Store_LastDataType(Argument arg) => _arg = arg;
            public override bool writesMemory() => _arg.isMemory();
            public override int operandLength() => _arg.getOperandLength();
            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context)
            {
                _arg.write(registers, addressSpace, args, context);
                return context;
            }
            public override string ToString() => string.Format(_arg.getDataType() == DataType.D16 ? "[__] → %s" : "[_] → %s", _arg.getLabel());
        }


        public OpcodeBuilder store(string target)
        {
            var arg = Argument.parse(target);

            if (lastDataType == DataType.D16 && arg.getLabel() == "(a16)")
            {
                ops.Add(new Store_a16_Op1(arg));
                ops.Add(new Store_a16_Op2(arg));

            }
            else if (lastDataType == arg.getDataType())
            {
                ops.Add(new Store_LastDataType(arg));
            }
            else
            {
                throw new InvalidOperationException($"Can't write {lastDataType} to {target}");
            }

            return this;
        }

        private class ProceedIf : Op
        {
            private readonly string _condition;
            public ProceedIf(string condition) => _condition = condition;
            public override bool proceed(Registers registers)
            {
                return _condition switch
                {
                    "NZ" => !registers.getFlags().isZ(),
                    "Z" => registers.getFlags().isZ(),
                    "NC" => !registers.getFlags().isC(),
                    "C" => registers.getFlags().isC(),
                    _ => false
                };
            }

            public override string ToString() => string.Format("? %s:", _condition);
        }

        public OpcodeBuilder proceedIf(string condition) 
        {
            ops.Add(new ProceedIf(condition));
            return this;
        }


        private class Push1 : Op
        {
            private readonly IntRegistryFunction _func;
            public Push1(IntRegistryFunction func) => _func = func;
            public override bool writesMemory() => true;
            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context)
            {
                registers.setSP(_func(registers.getFlags(), registers.getSP()));
                addressSpace.setByte(registers.getSP(), (context & 0xff00) >> 8);
                return context;
            }

            public override SpriteBug.CorruptionType? causesOemBug(Registers registers, int context)
            {
                return inOamArea(registers.getSP()) ? SpriteBug.CorruptionType.PUSH_1 : (SpriteBug.CorruptionType?) null;
            }

            public override string ToString() => "[_ ] → (SP--)";
        }

        private class Push2 : Op
        {
            private readonly IntRegistryFunction _func;
            public Push2(IntRegistryFunction func) => _func = func;
            public override bool writesMemory() => true;
            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context)
            {
                registers.setSP(_func(registers.getFlags(), registers.getSP()));
                addressSpace.setByte(registers.getSP(), context & 0x00ff);
                return context;
            }
            public override SpriteBug.CorruptionType? causesOemBug(Registers registers, int context)
            {
                return inOamArea(registers.getSP()) ? SpriteBug.CorruptionType.PUSH_2 : (SpriteBug.CorruptionType?) null;
            }


            public override string ToString() => "[ _] → (SP--)";
        }


        public OpcodeBuilder push() {
            var dec = ALU.findAluFunction("DEC", DataType.D16);
            ops.Add(new Push1(dec));
            ops.Add(new Push2(dec));
            return this;
        }


        private class Pop1 : Op
        {
            private readonly IntRegistryFunction _func;
            public Pop1(IntRegistryFunction func) => _func = func;
            public override bool readsMemory()
            {
                return true;
            }

            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context)
            {
                int lsb = addressSpace.getByte(registers.getSP());
                registers.setSP(_func(registers.getFlags(), registers.getSP()));
                return lsb;
            }


            public override SpriteBug.CorruptionType? causesOemBug(Registers registers, int context)
            {
                return inOamArea(registers.getSP()) ? SpriteBug.CorruptionType.POP_1 : (SpriteBug.CorruptionType?) null;
            }


            public override string ToString()
            {
                return string.Format("(SP++) → [ _]");
            }
        }

        private class Pop2 : Op
        {
            private readonly IntRegistryFunction _func;
            public Pop2(IntRegistryFunction func) => _func = func;
            public override bool readsMemory()
            {
                return true;
            }
        
            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context)
            {
                int msb = addressSpace.getByte(registers.getSP());
                registers.setSP(_func(registers.getFlags(), registers.getSP()));
                return context | (msb << 8);
            }


            public override SpriteBug.CorruptionType? causesOemBug(Registers registers, int context)
            {
                return inOamArea(registers.getSP()) ? SpriteBug.CorruptionType.POP_2 : (SpriteBug.CorruptionType?) null;
            }
        
            public override string ToString()
            {
                return string.Format("(SP++) → [_ ]");
            }
        }

        public OpcodeBuilder pop() {
            var inc = ALU.findAluFunction("INC", DataType.D16);
            lastDataType = DataType.D16;
            ops.Add(new Pop1(inc));
            ops.Add(new Pop2(inc));
            return this;
        }


        private class Alu1 : Op
        {
            private readonly BiIntRegistryFunction _func;
            private Argument _arg2;
            private readonly string _operation;
            private readonly DataType _lastDataType;

            public Alu1(BiIntRegistryFunction func, Argument arg2, string operation, DataType lastDataType)
            {
                _func = func;
                _arg2 = arg2;
                _operation = operation;
                _lastDataType = lastDataType;
            }

            public override bool readsMemory()
            {
                return _arg2.isMemory();
            }


            public override int operandLength()
            {
                return _arg2.getOperandLength();
            }


            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int v1)
            {
                var v2 = _arg2.read(registers, addressSpace, args);
                return _func(registers.getFlags(), v1, v2);
            }


            public override string ToString()
            {
                if (_lastDataType == DataType.D16)
                {
                    return string.Format("%s([__],%s) → [__]", _operation, _arg2);
                }

                return string.Format("%s([_],%s) → [_]", _operation, _arg2);
            }
        }

        public OpcodeBuilder alu(string operation, string argument2) {
            var arg2 = Argument.parse(argument2);
            var func = ALU.findAluFunction(operation, lastDataType, arg2.getDataType());
            ops.Add(new Alu1(func, arg2, operation, lastDataType));

            if (lastDataType == DataType.D16) {
                extraCycle();
            }
            return this;
        }

        private class Alu2 : Op
        {
            private readonly BiIntRegistryFunction _func;
            private readonly string _operation;
            private readonly int _d8Value;

            public Alu2(BiIntRegistryFunction func, string operation, int d8Value)
            {
                _func = func;
                _operation = operation;
                _d8Value = d8Value;
            }

            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int v1)
            {
                return _func(registers.getFlags(), v1, _d8Value);
            }


            public override string ToString()
            {
                return string.Format("%s(%d,[_]) → [_]", _operation, _d8Value);
            }
        }

        public OpcodeBuilder alu(string operation, int d8Value) {
            var func = ALU.findAluFunction(operation, lastDataType, DataType.D8);
            ops.Add(new Alu2(func, operation, d8Value));

            if (lastDataType == DataType.D16) {
                extraCycle();
            }

            return this;
        }


        private class Alu3 : Op
        {
            private readonly IntRegistryFunction _func;
            private readonly string _operation;
            private readonly DataType _lastDataType;

            public Alu3(IntRegistryFunction func, string operation, DataType lastDataType)
            {
                _func = func;
                _operation = operation;
                _lastDataType = lastDataType;
            }

            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int value)
            {
                return _func(registers.getFlags(), value);
            }


            public override SpriteBug.CorruptionType? causesOemBug(Registers registers, int context)
            {
                return OpcodeBuilder.causesOemBug(_func, context) ? SpriteBug.CorruptionType.INC_DEC : (SpriteBug.CorruptionType?) null;
            }

            public override string ToString()
            {
                if (_lastDataType == DataType.D16)
                {
                    return string.Format("%s([__]) → [__]", _operation);
                }
                else
                {
                    return string.Format("%s([_]) → [_]", _operation);
                }
            }
        }

        public OpcodeBuilder alu(string operation) {
            var func = ALU.findAluFunction(operation, lastDataType);
            ops.Add(new Alu3(func, operation, lastDataType));

            if (lastDataType == DataType.D16) {
                extraCycle();
            }
            return this;
        }

        private class AluHL : Op
        {
            private readonly IntRegistryFunction _func;
            public AluHL(IntRegistryFunction func)
            {
                _func = func;
            }

            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int value)
            {
                return _func(registers.getFlags(), value);
            }

            public override SpriteBug.CorruptionType? causesOemBug(Registers registers, int context)
            {
                return OpcodeBuilder.causesOemBug(_func, context) ? SpriteBug.CorruptionType.LD_HL : (SpriteBug.CorruptionType?) null;
            }

            public override string ToString()
            {
                return string.Format("%s(HL) → [__]");
            }
        }

        public OpcodeBuilder aluHL(string operation) {
            load("HL");
            ops.Add(new AluHL(ALU.findAluFunction(operation, DataType.D16)));
            store("HL");
            return this;
        }


        private class BitHL : Op
        {
            private readonly int _bit;
            public BitHL(int bit)
            {
                _bit = bit;
            }

            public override bool readsMemory()
            {
                return true;
            }


            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context)
            {
                var value = addressSpace.getByte(registers.getHL());
                var flags = registers.getFlags();
                flags.setN(false);
                flags.setH(true);
                if (_bit < 8)
                {
                    flags.setZ(!getBit(value, _bit));
                }
                return context;
            }


            public override string ToString()
            {
                return string.Format("BIT(%d,HL)", _bit);
            }
        }

        public OpcodeBuilder bitHL(int bit) {
            ops.Add(new BitHL(bit));
            return this;
        }

        private class ClearZ : Op
        {
            public override int execute(Registers registers, AddressSpace addressSpace, int[] args, int context)
            {
                registers.getFlags().setZ(false);
                return context;
            }

            public override string ToString() => string.Format("0 → Z");
        }

        public OpcodeBuilder clearZ() {
            ops.Add(new ClearZ());
            return this;
        }

        private class SwitchInterrupts : Op
        {
            private readonly bool _enable;
            private readonly bool _withDelay;

            public SwitchInterrupts(bool enable, bool withDelay)
            {
                _enable = enable;
                _withDelay = withDelay;
            }

            public override void switchInterrupts(InterruptManager interruptManager)
            {
                if (_enable)
                {
                    interruptManager.enableInterrupts(_withDelay);
                }
                else
                {
                    interruptManager.disableInterrupts(_withDelay);
                }
            }


            public override string ToString()
            {
                return (_enable ? "enable" : "disable") + " interrupts";
            }
        }

        public OpcodeBuilder switchInterrupts(bool enable, bool withDelay) {
            ops.Add(new SwitchInterrupts(enable, withDelay));
            return this;
        }

        public OpcodeBuilder op(Op op) {
            ops.Add(op);
            return this;
        }

        private class ExtraCycleOp : Op
        {
            public override bool readsMemory() => true;
            public override string ToString() => "wait cycle";
        }

        public OpcodeBuilder extraCycle() {
            ops.Add(new ExtraCycleOp());
            return this;
        }

        private class ForceFinish : Op
        {
            public override bool forceFinishCycle() => true;
            public override string ToString() => "finish cycle";
        }

        public OpcodeBuilder forceFinish() {
            ops.Add(new ForceFinish());
            return this;
        }

        public Opcode build() {
            return new Opcode(this);
        }

        public int getOpcode() {
            return opcode;
        }

        public string getLabel() {
            return label;
        }

        public List<Op> getOps() {
            return ops;
        }

    
        public string toString() {
            return label;
        }

        public static bool causesOemBug(IntRegistryFunction function, int context) {
            return OEM_BUG.Contains(function) && inOamArea(context);
        }

        public static bool inOamArea(int address) {
            return address >= 0xfe00 && address <= 0xfeff;
        }
    }
}