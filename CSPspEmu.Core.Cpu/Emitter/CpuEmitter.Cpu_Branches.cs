﻿#define BRANCH_FLAG_AS_LOCAL
#define ENABLE_NATIVE_CALLS

using System;
using SafeILGenerator.Ast;
using SafeILGenerator.Ast.Nodes;

namespace CSPspEmu.Core.Cpu.Emitter
{
	public sealed partial class CpuEmitter
	{
		public AstNodeStm _branch_likely(AstNodeStm Code)
		{
			return ast.If(BranchFlag(), Code);
		}

		// Code executed after the delayed slot.
		public AstNodeStm _branch_post(AstLabel BranchLabel, uint BranchPC)
		{
			if (this.AndLink)
			{
				return ast.If(
					BranchFlag(),
					ast.StatementsInline(
						ast.AssignGPR(31, BranchPC + 8),
#if ENABLE_NATIVE_CALLS
						CallFixedAddress(BranchPC)
#else
						ast.GotoAlways(BranchLabel)
#endif
					)
				);
			}
			else
			{
				return ast.GotoIfTrue(BranchLabel, BranchFlag());
			}
		}

		bool AndLink = false;
		uint BranchPC = 0;

#if BRANCH_FLAG_AS_LOCAL
		private AstLocal BranchFlagLocal = null;
#endif

		private AstNodeExprLValue BranchFlag()
		{
#if BRANCH_FLAG_AS_LOCAL
			if (BranchFlagLocal == null)
			{
				BranchFlagLocal = AstLocal.Create<bool>("BranchFlag");
			}
			return ast.Local(BranchFlagLocal);
#else
			return REG("BranchFlag");
#endif
		}

		private AstNodeStm AssignBranchFlag(AstNodeExpr Expr, bool AndLink = false)
		{
			this.AndLink = AndLink;
			this.BranchPC = PC;
			return ast.Assign(BranchFlag(), ast.Cast<bool>(Expr, Explicit: false));
		}

		/////////////////////////////////////////////////////////////////////////////////////////////////
		// beq(l)     : Branch on EQuals (Likely).
		// bne(l)     : Branch on Not Equals (Likely).
		// btz(al)(l) : Branch on Less Than Zero (And Link) (Likely).
		// blez(l)    : Branch on Less Or Equals than Zero (Likely).
		// bgtz(l)    : Branch on Great Than Zero (Likely).
		// bgez(al)(l): Branch on Greater Equal Zero (And Link) (Likely).
		/////////////////////////////////////////////////////////////////////////////////////////////////
		public AstNodeStm beq() { return AssignBranchFlag(ast.Binary(ast.GPR_s(RS), "==", ast.GPR_s(RT))); }
		public AstNodeStm beql() { return beq(); }
		public AstNodeStm bne() { return AssignBranchFlag(ast.Binary(ast.GPR_s(RS), "!=", ast.GPR_s(RT))); }
		public AstNodeStm bnel() { return bne(); }
		public AstNodeStm bltz() { return AssignBranchFlag(ast.Binary(ast.GPR_s(RS), "<", 0)); }
		public AstNodeStm bltzl() { return bltz(); }
		[PspUntested]
		public AstNodeStm bltzal() { return AssignBranchFlag(ast.Binary(ast.GPR_s(RS), "<", 0), AndLink: true); }
		public AstNodeStm bltzall() { return bltzal(); }
		public AstNodeStm blez() { return AssignBranchFlag(ast.Binary(ast.GPR_s(RS), "<=", 0)); }
		public AstNodeStm blezl() { return blez(); }
		public AstNodeStm bgtz() { return AssignBranchFlag(ast.Binary(ast.GPR_s(RS), ">", 0)); }
		public AstNodeStm bgtzl() { return bgtz(); }
		public AstNodeStm bgez() { return AssignBranchFlag(ast.Binary(ast.GPR_s(RS), ">=", 0)); }
		public AstNodeStm bgezl() { return bgez(); }
		[PspUntested]
		public AstNodeStm bgezal() { return AssignBranchFlag(ast.Binary(ast.GPR_s(RS), ">=", 0), AndLink: true); }
		public AstNodeStm bgezall() { return bgezal(); }

		public bool PopulateCallStack { get { return !(CpuProcessor.Memory.HasFixedGlobalAddress) && CpuProcessor.CpuConfig.TrackCallStack; } }

		/*
		private AstNodeStm _popstack()
		{
			//if (PopulateCallStack && (RS == 31)) return ast.Statement(ast.CallInstance(CpuThreadStateArgument(), (Action)CpuThreadState.Methods.CallStackPop));
			return ast.Statement();
		}

		private AstNodeStm _pushstack()
		{
			//if (PopulateCallStack) return ast.Statement(ast.CallInstance(CpuThreadStateArgument(), (Action<uint>)CpuThreadState.Methods.CallStackPush, PC));
			return ast.Statement();
		}
		*/

		private AstNodeStm _link()
		{
			return ast.AssignGPR(31, ast.Immediate(PC + 8));
		}

		private AstNodeStm JumpToAddress(AstNodeExpr Address)
		{
#if true
			return ast.Statement(ast.CallTail(ast.MethodCacheInfoCallDynamicPC(Address)));
#else
			return ast.Statements(
				MipsMethodEmitter.AssignREG("PC", Address),
				ast.Return()
			);
#endif
		}

		private AstNodeStm CallAddress(AstNodeExpr Address)
		{
			return ast.StatementsInline(
				_link(),
#if ENABLE_NATIVE_CALLS
				ast.Statement(ast.MethodCacheInfoCallDynamicPC(Address))
#else
				JumpToAddress(Address)
#endif
			);
		}

		private AstNodeStm JumpToFixedAddress(uint Address)
		{
#if true
			return ast.Statement(
				ast.CallTail(ast.MethodCacheInfoCallStaticPC(CpuProcessor, Address))
			);
#else
			return ast.StatementsInline(
				MipsMethodEmitter.AssignREG("PC", Address),
				ast.Return()
			);
#endif
		}

		private AstNodeStm CallFixedAddress(uint Address)
		{
			return ast.StatementsInline(
				_link(),
#if ENABLE_NATIVE_CALLS
				ast.Statement(ast.MethodCacheInfoCallStaticPC(CpuProcessor, Address))
#else
				JumpToFixedAddress(Address)
#endif
			);
		}

		//static public void DumpStackTrace()
		//{
		//	Console.WriteLine(Environment.StackTrace);
		//}

		private AstNodeStm ReturnFromFunction(AstNodeExpr AstNodeExpr)
		{
#if ENABLE_NATIVE_CALLS
			return ast.StatementsInline(
				ast.AssignREG("PC", ast.GPR(31)),
				ast.GetTickCall(),
				ast.Return()
			);
#else
			return JumpToAddress(AstNodeExpr);
#endif
		}

		/////////////////////////////////////////////////////////////////////////////////////////////////
		// j(al)(r): Jump (And Link) (Register)
		/////////////////////////////////////////////////////////////////////////////////////////////////
		public AstNodeStm j() { return this.JumpToFixedAddress(Instruction.GetJumpAddress(this.Memory, PC)); }
		public AstNodeStm jal() { return this.CallFixedAddress(Instruction.GetJumpAddress(this.Memory, PC)); }
		public AstNodeStm jr() {
			if (RS == 31)
			{
				return ReturnFromFunction(ast.GPR_u(RS));
			}
			else
			{
				return JumpToAddress(ast.GPR_u(RS));
			}
		}
		public AstNodeStm jalr() { return this.CallAddress(ast.GPR_u(RS)); }
	}
}