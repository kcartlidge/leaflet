﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZMachine.Instructions;
using ZMachine.Memory;

namespace ZMachine
{
    public class Interpreter
    {
        private const int UNUSED_RETURN_VALUE = -1;

        public void LoadStory(Stream storyStream)
        {
            MainMemory = new ZMemory(storyStream);
            FrameStack = new Stack<Routine>();
            //ProgramCounter = (int)MainMemory.Header.PCStart;

            LoadNewFrame(MainMemory.Header.PCStart - 1, 0, null);
        }

        public int ProgramCounter { get; set; }
        public ZMemory MainMemory { get; set; }

        public ZProcessor Processor { get; set; }
        public Stack<Routine> FrameStack { get; set; }

        /// <summary>
        /// Reads the value of a ZVariable
        /// </summary>
        /// <param name="variable">the variable to read</param>
        /// <param name="inPlace">if the value is in the evaluation stack, whether the read should pop values or read them in place. Otherwise
        /// it has no effect</param>
        /// <returns></returns>
        public int ReadVariable(ZVariable variable, bool inPlace = false)
        {
            switch (variable.Location)
            {
                case ZVariableLocation.Global:
                    return MainMemory.GlobalVariables[variable.Value];
                case ZVariableLocation.Local:
                    return CurrentRoutineFrame.Locals[variable.Value];
                case ZVariableLocation.Stack:
                    var varStack = CurrentRoutineFrame.EvaluationStack;
                    return inPlace ? varStack.Peek() : varStack.Pop();
                default:
                    throw new ArgumentOutOfRangeException(nameof(variable.Location));
            }
        }

        /// <summary>
        /// Sets the value of a variable in memory
        /// </summary>
        /// <param name="variable">the variable to set</param>
        /// <param name="value">the value</param>
        /// <param name="inPlace">If setting a value on the stack, this indicates whether to change 
        /// the top value in place instead of pushing a new value. Has no effect on local or globals.</param>
        public void SetVariable(ZVariable variable, ushort value, bool inPlace = false)
        {
            switch (variable.Location)
            {
                case ZVariableLocation.Global:
                    MainMemory.GlobalVariables[variable.Value] = value;
                    break;
                case ZVariableLocation.Local:
                    CurrentRoutineFrame.Locals[variable.Value] = value;
                    break;
                case ZVariableLocation.Stack:
                    var varStack = CurrentRoutineFrame.EvaluationStack;
                    if (inPlace)
                    {
                        varStack.Pop();
                    }
                    varStack.Push(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(variable.Location));
            }
        }

        public ZVariable Dereference(ZVariable zVarRef)
        {
            byte zvarVal = (byte)ReadVariable(zVarRef);
            return new ZVariable(zvarVal);
        }

        public int GetOperandValue(ZOperand operand)
        {
            switch (operand.Type)
            {
                case OperandTypes.LargeConstant:
                case OperandTypes.SmallConstant:
                    return (int)operand.Constant;
                case OperandTypes.Variable:
                    return ReadVariable(operand.Variable);
                default:
                    throw new NotImplementedException($"Reading from {operand.Type} not implemented");
            }
        }

        public void Print(bool printFrames = false)
        {
            if (printFrames)
            {
                Console.WriteLine("---------------------");
                if (FrameStack.Count > 0)
                {

                    foreach (var frame in FrameStack)
                    {
                        Console.WriteLine(frame);
                    }
                }
                else
                {
                    Console.WriteLine("<no frames>");
                }

            }
            ZOpcode opcode = new ZOpcode(MainMemory.Bytes, ProgramCounter);
            Console.WriteLine(opcode);
        }

        public void ExecuteCurrentInstruction()
        {
            ZOpcode opcode = new ZOpcode(MainMemory.Bytes, ProgramCounter);
            switch (opcode.Definition.Name)
            {
                case "call":
                    Handle_Call(opcode);
                    break;
                case "ret":
                    Handle_Return(opcode);
                    break;
                case "add":
                    ExecInstruction(opcode, op =>
                    {
                        short a = (short)GetOperandValue(op.Operands[0]);
                        short b = (short)GetOperandValue(op.Operands[1]);
                        return a + b;
                    });
                    break;
                case "sub":
                    ExecInstruction(opcode, op =>
                    {
                        short a = (short)GetOperandValue(op.Operands[0]);
                        short b = (short)GetOperandValue(op.Operands[1]);
                        return a - b;
                    });
                    break;
                case "mul":
                    ExecInstruction(opcode, op =>
                    {
                        short a = (short)GetOperandValue(op.Operands[0]);
                        short b = (short)GetOperandValue(op.Operands[1]);
                        return a * b;
                    });
                    break;
                case "div":
                    ExecInstruction(opcode, op =>
                    {
                        short a = (short)GetOperandValue(op.Operands[0]);
                        short b = (short)GetOperandValue(op.Operands[1]);
                        return a / b;
                    });
                    break;
                case "mod":
                    ExecInstruction(opcode, op =>
                    {
                        short a = (short)GetOperandValue(op.Operands[0]);
                        short b = (short)GetOperandValue(op.Operands[1]);
                        return a % b;
                    });
                    break;
                case "je":
                    ExecInstruction(opcode, op =>
                    {
                        // je a b c d ? (label)
                        short a = (short)GetOperandValue(op.Operands[0]);
                        for (int i = 1; i < op.Operands.Count; i++)
                        {
                            // is true if the first operand matches any of the others
                            if (a == GetOperandValue(op.Operands[i]))
                            {
                                return 1;
                            }
                        }
                        return 0;   // didn't match so don't branch
                    });
                    break;
                case "jz":  // jz a ?(label)
                    ExecInstruction(opcode, op => GetOperandValue(op.Operands[0]) == 0 ? 1 : 0);
                    break;
                case "jl":  // jl a b ?(label)
                    ExecInstruction(opcode, op => (short)GetOperandValue(op.Operands[0]) < (short)GetOperandValue(op.Operands[0]) ? 1 : 0);
                    break;
                case "jg":  // jg a b ?(label)
                    ExecInstruction(opcode, op => (short)GetOperandValue(op.Operands[0]) > (short)GetOperandValue(op.Operands[0]) ? 1 : 0);
                    break;
                case "jump":    // jump ?(label)
                    int jumpAddress = GetOperandValue(opcode.Operands[0]);
                    ProgramCounter = jumpAddress;
                    break;
                case "loadb":   // loadb array byte-index -> (result)
                    ExecInstruction(opcode, op =>
                    {
                        int arrayAddress = GetOperandValue(op.Operands[0]);
                        int byteIndex = GetOperandValue(op.Operands[1]);
                        return MainMemory.Bytes[arrayAddress + byteIndex];
                    });
                    break;
                case "loadw":   // loadw array word-index -> (result)
                    ExecInstruction(opcode, op =>
                    {
                        int arrayAddress = GetOperandValue(op.Operands[0]);
                        int wordIndex = GetOperandValue(op.Operands[1]);
                        var wob = new WordOverByteArray(MainMemory.Bytes, arrayAddress);
                        return wob[wordIndex];

                    });
                    break;
                case "storeb":  // storeb array byte-index value
                    ExecInstruction(opcode, op =>
                    {
                        int arrayAddress = GetOperandValue(op.Operands[0]);
                        int byteIndex = GetOperandValue(op.Operands[1]);
                        byte value = (byte)GetOperandValue(op.Operands[2]);
                        MainMemory.Bytes[arrayAddress + byteIndex] = value;
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "storew":  // storew array word-index value
                    ExecInstruction(opcode, op =>
                    {
                        int arrayAddress = GetOperandValue(op.Operands[0]);
                        int wordIndex = GetOperandValue(op.Operands[1]);
                        ushort value = (ushort)GetOperandValue(op.Operands[2]);

                        var wob = new WordOverByteArray(MainMemory.Bytes, arrayAddress);
                        wob[wordIndex] = value;
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "load":    // load (variable) -> (result)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);
                        var actualZVar = GetDereferencedFirstZVar(op);
                        int value = ReadVariable(actualZVar, false);
                        return value;
                    });
                    break;
                case "store":   // store (variable) value
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 2);
                        var actualZVar = GetDereferencedFirstZVar(op);
                        int valueToStore = GetOperandValue(op.Operands[1]);
                        SetVariable(actualZVar, (ushort)valueToStore, false);
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "pull":    // pull (variable)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);
                        var actualZVar = GetDereferencedFirstZVar(op);
                        int value = ReadVariable(actualZVar, true);
                        return value;
                    });
                    break;
                case "inc":    // inc (variable)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);
                        var actualZVar = GetDereferencedFirstZVar(op);
                        short value = (short)ReadVariable(actualZVar, true);
                        value++;
                        SetVariable(actualZVar, (ushort)value, true);
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "dec":    // dec (variable)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);
                        var actualZVar = GetDereferencedFirstZVar(op);
                        short value = (short)ReadVariable(actualZVar, true);
                        value--;
                        SetVariable(actualZVar, (ushort)value, true);
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "inc_chk":    // inc_chk (variable) value ?(label)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 2);

                        // do the increment
                        var actualZVar = GetDereferencedFirstZVar(op);
                        short value = (short)ReadVariable(actualZVar, true);
                        value++;
                        SetVariable(actualZVar, (ushort)value, true);

                        // do the compare
                        short compareVal = (short)GetOperandValue(op.Operands[1]);
                        return value == compareVal ? 1 : 0;
                    });
                    break;
                case "dec_chk":    // inc_chk (variable) value ?(label)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 2);

                        // do the decrement
                        var actualZVar = GetDereferencedFirstZVar(op);
                        short value = (short)ReadVariable(actualZVar, true);
                        value--;
                        SetVariable(actualZVar, (ushort)value, true);

                        // do the compare
                        short compareVal = (short)GetOperandValue(op.Operands[1]);
                        return value == compareVal ? 1 : 0;
                    });
                    break;
                case "jin":    // jin obj1 obj2 ?(label)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 2);

                        int childId = GetOperandValue(op.Operands[0]);
                        int parentId = GetOperandValue(op.Operands[1]);

                        return MainMemory.ObjectTree.IsValidChild(parentId, childId) ? 1 : 0;
                    });
                    break;
                case "get_child":    // get_child object -> (result) ?(label)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);

                        int objectId = GetOperandValue(op.Operands[0]);
                        return MainMemory.ObjectTree.GetChild(objectId);
                    });
                    break;
                case "get_sibling":    // get_sibling object -> (result) ?(label)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);

                        int objectId = GetOperandValue(op.Operands[0]);
                        return MainMemory.ObjectTree.GetSiblingId(objectId);
                    });
                    break;
                case "get_parent":    // get_parent object -> (result)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);

                        int objectId = GetOperandValue(op.Operands[0]);
                        return MainMemory.ObjectTree.GetParent(objectId);
                    });
                    break;
                case "insert_obj":  // insert_obj object destination
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 2);
                        int objectId = GetOperandValue(op.Operands[0]);
                        int newParentId = GetOperandValue(op.Operands[1]);
                        MainMemory.ObjectTree.ReparentObject(objectId, newParentId);
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "print":   // print <literal-string>
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 0);
                        Debug.Assert(op.Definition.HasText == true);
                        // Print the quoted (literal) Z-encoded string.
                        Console.Write(opcode.Text);
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "print_obj":   // print_obj object
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);
                        // takes an object number and prints its short name
                        int objID = GetOperandValue(opcode.Operands[0]);
                        var obj = MainMemory.ObjectTree.GetObject(objID);
                        Console.Write(obj.ShortName);
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "print_addr":  // print_addr byte-address-of-string"
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);
                        // Print (Z-encoded) string at given byte address
                        int address = GetOperandValue(opcode.Operands[0]);
                        var obj = new ZStringBuilder(MainMemory.Bytes, address);
                        Console.Write(obj.ToString());
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "print_paddr":  // print_paddr packed-address-of-string
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);
                        // Print (Z-encoded) string at given byte address
                        int address = GetOperandValue(opcode.Operands[0]) * 2;
                        var obj = new ZStringBuilder(MainMemory.Bytes, address);
                        Console.Write(obj.ToString());
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "print_ret":  // print_ret <literal-string>
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 0);
                        Debug.Assert(op.Definition.HasText == true);
                        // Print the quoted (literal) Z-encoded string, then print
                        // a new-line and then return true (i.e., 1). 
                        Console.WriteLine(opcode.Text);
                        return 1;
                    });
                    break;
                case "print_num":  // print_num value
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);
                        // Print(signed) number in decimal.
                        int val = GetOperandValue(opcode.Operands[0]);
                        Console.WriteLine(val.ToString("N"));
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "print_char":  // print_char output-character-code
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 1);
                        // Print a ZSCII character
                        int val = GetOperandValue(opcode.Operands[0]);
                        Debug.Assert(val >= 0 && val < 1023, "print_char out of range");
                        Console.Write(ZStringBuilder.GetChar((ushort)val));
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "new_line":  // new_line
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 0);
                        Console.WriteLine();
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "test":    // "test bitmap flags ? (label)"
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 2);
                        // Jump if all of the flags in bitmap are set(i.e. if bitmap & flags == flags).
                        int bitmap = GetOperandValue(opcode.Operands[0]);
                        int flags = GetOperandValue(opcode.Operands[1]);
                        int result = (bitmap & flags) == flags ? 1 : 0;
                        return result;
                    });
                    break;
                case "and": // and a b -> (result)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 2);
                        // Bitwise AND
                        int a = GetOperandValue(opcode.Operands[0]);
                        int b = GetOperandValue(opcode.Operands[1]);
                        return a & b;
                    });
                    break;
                case "or": // or a b -> (result)
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 2);
                        // Bitwise OR
                        int a = GetOperandValue(opcode.Operands[0]);
                        int b = GetOperandValue(opcode.Operands[1]);
                        return a | b;
                    });
                    break;
                case "pop": // pop
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 0);
                        // Throws away the top item on the stack. (This was useful 
                        // to lose unwanted routine call results in early Versions.)
                        CurrentRoutineFrame.EvaluationStack.Pop(); 
                        return UNUSED_RETURN_VALUE;
                    });
                    break;
                case "ret_popped": // ret_popped
                    ExecInstruction(opcode, op =>
                    {
                        Debug.Assert(op.OperandType.Count == 0);
                        // Pops top of stack and returns that. (This is equivalent 
                        // to ret sp, but is one byte cheaper.) 
                        return CurrentRoutineFrame.EvaluationStack.Pop(); 
                    });
                    break;
                case "catch":
                    throw new NotImplementedException($"TODO: catch instruction not yet implemented");
                default:
                    throw new NotImplementedException($"Opcode {opcode.Identifier}:{opcode.Definition.Name} not implemented yet");
            }
        }

        private ZVariable GetDereferencedFirstZVar(ZOpcode opcode)
        {
            Debug.Assert(opcode.OperandType.Count >= 1);

            // first instruction is a dereferenced ZVar
            byte zVarValue = (byte)GetOperandValue(opcode.Operands[0]);
            ZVariable zvarRef = new ZVariable(zVarValue);
            return zvarRef;
        }

        private Routine CurrentRoutineFrame
        {
            get
            {
                return FrameStack.Peek();
            }
        }

        void Handle_Call(ZOpcode opcode)
        {
            uint callAddress = opcode.Operands[0].Constant;
            int nextInstruction = ProgramCounter += opcode.LengthInBytes;
            if (callAddress == 0)
            {
                SetVariable(opcode.Store, 0);
                ProgramCounter = nextInstruction;
            }
            else
            {
                LoadNewFrame((int)callAddress, nextInstruction, opcode.Store, opcode.Operands.Skip(1).ToArray());
            }
        }

        void Handle_Return(ZOpcode opcode)
        {
            // get the return value
            int retVal = GetOperandValue(opcode.Operands[0]);

            // remove the current frame from the stack
            var oldFrame = FrameStack.Pop();

            // put the return value wherever the oldFrame required
            SetVariable(oldFrame.Store, (ushort)retVal);

            // update the instruction counter
            ProgramCounter = oldFrame.ReturnAddress;
        }

        private void LoadNewFrame(int newAddress, int returnAddress, ZVariable returnStore, params ZOperand[] operands)
        {
            // initialize a new frame
            var initLocals = operands.Select(op => (ushort)GetOperandValue(op)).ToArray();
            Routine newRoutine = new Routine(MainMemory.Bytes, newAddress, returnAddress, returnStore, initLocals);
            FrameStack.Push(newRoutine);

            // update the instruction counter
            ProgramCounter = newRoutine.FirstInstructionAddress;
        }

        /// <summary>
        /// Executes the supplied handler method and then optionally stores the return
        /// value and then moves the instruction counter forward (either incrmentally or as a 
        /// valid branch).
        /// </summary>
        /// <param name="opcode">The opcode of the instruction which determines the Store and Branch
        /// functionality</param>
        /// <param name="handler">The primary logic of the instruction (minus Store and Branch
        /// functionality). If the opcode has a Store location, the return value from the handler
        /// will be put there. If the opcode is a branch, the return value is also used to determine
        /// whether the branch will happen</param>
        private void ExecInstruction(ZOpcode opcode, Func<ZOpcode, int> handler)
        {
            int result = handler(opcode);

            if (opcode.Definition.HasStore)
            {
                SetVariable(opcode.Store, (ushort)result);
            }
            BranchOrNext(opcode, result);
        }

        /// <summary>
        /// Shifts the state to the next instruction depending on whether the 
        /// current opcode is a branching instruction or just needs to increment
        /// the instruction counter to the next instruction sequentially
        /// </summary>
        /// <param name="opcode"></param>
        private void BranchOrNext(ZOpcode opcode, int branchValue)
        {
            BranchOffset branch = opcode.BranchOffset;
            if (branch != null)
            {
                // read the resulting value from the opcode store variable
                bool branchIfIfOne = branch.WhenTrue;

                // test the branch condition against the stored value
                if ((branchIfIfOne && branchValue != 0) ||
                    (!branchIfIfOne && branchValue == 0))
                {
                    ProgramCounter = opcode.BranchToAddress;
                    return;
                }
            }

            // just move to the next instruction 
            ProgramCounter += opcode.LengthInBytes;
        }
    }
}