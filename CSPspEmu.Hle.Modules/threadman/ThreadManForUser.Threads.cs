﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CSharpUtils;
using CSPspEmu.Core.Cpu;
using CSPspEmu.Hle.Managers;

namespace CSPspEmu.Hle.Modules.threadman
{
	unsafe public partial class ThreadManForUser
	{
		private HleThread GetThreadById(int ThreadId)
		{
			HleThread HleThread = HleState.ThreadManager.GetThreadById(ThreadId);
			if (HleThread == null) throw (new SceKernelException(SceKernelErrors.ERROR_KERNEL_NOT_FOUND_THREAD));
			return HleThread;
		}

		/// <summary>
		/// Create a thread
		/// </summary>
		/// <example>
		/// SceUID thid;
		/// thid = sceKernelCreateThread("my_thread", threadFunc, 0x18, 0x10000, 0, NULL);
		/// </example>
		/// <param name="Name">An arbitrary thread name.</param>
		/// <param name="EntryPoint">The thread function to run when started.</param>
		/// <param name="InitPriority">The initial priority of the thread. Less if higher priority.</param>
		/// <param name="StackSize">The size of the initial stack.</param>
		/// <param name="Attribute">The thread attributes, zero or more of ::PspThreadAttributes.</param>
		/// <param name="Option">Additional options specified by ::SceKernelThreadOptParam.</param>
		/// <returns>UID of the created thread, or an error code.</returns>
		[HlePspFunction(NID = 0x446D8DE6, FirmwareVersion = 150)]
		public uint sceKernelCreateThread(CpuThreadState CpuThreadState, string Name, uint /*SceKernelThreadEntry*/ EntryPoint, int InitPriority, int StackSize, PspThreadAttributes Attribute, SceKernelThreadOptParam* Option)
		{
			var Thread = HleState.ThreadManager.Create();
			Thread.Name = Name;
			Thread.Info.PriorityCurrent = InitPriority;
			Thread.Info.PriorityInitially = InitPriority;
			Thread.Attribute = Attribute;
			Thread.GP = CpuThreadState.GP;
			Thread.Info.EntryPoint = (SceKernelThreadEntry)EntryPoint;
			Thread.Stack = HleState.MemoryManager.GetPartition(HleMemoryManager.Partitions.User).Allocate(StackSize, MemoryPartition.Anchor.High, Alignment: 0x100);
			if (!Thread.Attribute.HasFlag(PspThreadAttributes.NoFillStack))
			{
				HleState.MemoryManager.Memory.WriteRepeated1(0xFF, Thread.Stack.Low, Thread.Stack.Size);
				//Console.Error.WriteLine("-------------------------------------------------");
				//Console.Error.WriteLine("'{0}', '{1}'", StackSize, Thread.Stack.Size);
				//Console.Error.WriteLine("-------------------------------------------------");
			}
			Thread.Info.StackPointer = Thread.Stack.High;
			Thread.Info.StackSize = StackSize;

			// Used K0 from parent thread.
			// @FAKE. K0 should be preserved between thread calls. Though probably not modified by user modules.
			Thread.CpuThreadState.CopyRegistersFrom(HleState.ThreadManager.Current.CpuThreadState);

			Thread.CpuThreadState.PC = (uint)EntryPoint;
			Thread.CpuThreadState.GP = (uint)CpuThreadState.GP;
			Thread.CpuThreadState.SP = (uint)(Thread.Stack.High);
			Thread.CpuThreadState.RA = (uint)0x08000000;
			Thread.CurrentStatus = HleThread.Status.Stopped;
			//Thread.CpuThreadState.RA = (uint)0;

			//Console.WriteLine("STACK: {0:X}", Thread.CpuThreadState.SP);

			return (uint)Thread.Id;
		}

		/// <summary>
		/// Start a created thread
		/// </summary>
		/// <param name="ThreadId">Thread id from sceKernelCreateThread</param>
		/// <param name="ArgumentsLength">Length of the data pointed to by argp, in bytes</param>
		/// <param name="ArgumentsPointer">Pointer to the arguments.</param>
		/// <returns></returns>
		[HlePspFunction(NID = 0xF475845D, FirmwareVersion = 150)]
		public int sceKernelStartThread(CpuThreadState CpuThreadState, int ThreadId, uint ArgumentsLength, uint ArgumentsPointer)
		{
			var ThreadToStart = GetThreadById((int)ThreadId);
			//Console.WriteLine("LEN: {0:X}", ArgumentsLength);
			//Console.WriteLine("PTR: {0:X}", ArgumentsPointer);
			ThreadToStart.CpuThreadState.GPR[4] = (int)ArgumentsLength;
			ThreadToStart.CpuThreadState.GPR[5] = (int)ArgumentsPointer;
			ThreadToStart.CurrentStatus = HleThread.Status.Ready;

			// Schedule new thread?
			HleState.ThreadManager.ScheduleNext(ThreadToStart);
			CpuThreadState.Yield();
			return 0;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="CpuThreadState"></param>
		/// <param name="HandleCallbacks"></param>
		/// <returns></returns>
		private int _sceKernelSleepThreadCB(CpuThreadState CpuThreadState, bool HandleCallbacks)
		{
			var ThreadToSleep = HleState.ThreadManager.Current;
			ThreadToSleep.ChangeWakeUpCount(-1, null);
			return 0;
		}

		/// <summary>
		/// Sleep thread until sceKernelWakeUp is called.
		/// </summary>
		/// <returns>Less than zero on error</returns>
		[HlePspFunction(NID = 0x9ACE131E, FirmwareVersion = 150)]
		public int sceKernelSleepThread(CpuThreadState CpuThreadState)
		{

			return _sceKernelSleepThreadCB(CpuThreadState, HandleCallbacks: false);
		}

		/// <summary>
		/// Sleep thread but service any callbacks as necessary
		/// </summary>
		/// <example>
		///		// Once all callbacks have been setup call this function
		///		sceKernelSleepThreadCB();
		/// </example>
		/// <returns></returns>
		[HlePspFunction(NID = 0x82826F70, FirmwareVersion = 150)]
		public int sceKernelSleepThreadCB(CpuThreadState CpuThreadState)
		{
			return _sceKernelSleepThreadCB(CpuThreadState, HandleCallbacks: true);
		}

		/// <summary>
		/// Get the current thread Id
		/// </summary>
		/// <returns>The thread id of the calling thread.</returns>
		[HlePspFunction(NID = 0x293B45B8, FirmwareVersion = 150)]
		public int sceKernelGetThreadId()
		{
			if (HleState.ThreadManager.Current == null) return 0;
			return HleState.ThreadManager.Current.Id;
		}

		/// <summary>
		/// Get the status information for the specified thread.
		/// </summary>
		/// <example>
		///		SceKernelThreadInfo status;
		///		status.size = sizeof(SceKernelThreadInfo);
		///		if (sceKernelReferThreadStatus(thid, &status) == 0) { Do something... }
		/// </example>
		/// <param name="thid">Id of the thread to get status</param>
		/// <param name="info">
		///		Pointer to the info structure to receive the data.
		///		Note: The structures size field should be set to
		///		sizeof(SceKernelThreadInfo) before calling this function.
		///	</param>
		/// <returns>0 if successful, otherwise the error code.</returns>
		[HlePspFunction(NID = 0x17C1684E, FirmwareVersion = 150)]
		public int sceKernelReferThreadStatus(int ThreadId, SceKernelThreadInfo* SceKernelThreadInfo)
		{
			var Thread = GetThreadById(ThreadId);
			*SceKernelThreadInfo = Thread.Info;
			return 0;
		}

		/// <summary>
		/// Wake a thread previously put into the sleep state.
		/// </summary>
		/// <remarks>
		/// This function increments a wakeUp count and sceKernelSleep(CB) decrements it.
		/// So when calling sceKernelSleep(CB) if this function have been executed before one or more times,
		/// the thread won't sleep until Sleeps is executed as many times as sceKernelWakeupThread.
		/// 
		/// ?? This waits until the thread has been awaken? TO CONFIRM.
		/// </remarks>
		/// <param name="thid">UID of the thread to wake.</param>
		/// <returns>Success if greater or equal 0, an error if less than 0.</returns>
		[HlePspFunction(NID = 0xD59EAD2F, FirmwareVersion = 150)]
		public int sceKernelWakeupThread(int ThreadId)
		{
			var ThreadCurrent = HleState.ThreadManager.Current;
			var ThreadToWakeUp = HleState.ThreadManager.GetThreadById(ThreadId);
			ThreadToWakeUp.ChangeWakeUpCount(+1, ThreadCurrent);
			return 0;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="ThreadId"></param>
		/// <param name="Timeout"></param>
		/// <returns></returns>
		private int _sceKernelWaitThreadEndCB(int ThreadId, uint* Timeout, bool HandleCallbacks)
		{
			var ThreadToWaitEnd = GetThreadById(ThreadId);

			if (ThreadToWaitEnd.CurrentStatus == HleThread.Status.Stopped)
			{
				return 0;
			}

			if (ThreadToWaitEnd.CurrentStatus == HleThread.Status.Killed)
			{
				return 0;
			}

			HleState.ThreadManager.Current.SetWaitAndPrepareWakeUp(HleThread.WaitType.None, "sceKernelWaitThreadEnd", WakeUpCallback =>
			{
				if (Timeout != null)
				{
					Console.Error.WriteLine("_sceKernelWaitThreadEndCB Timeout not implemented!!");
					//throw (new NotImplementedException());
				}

				Console.WriteLine("Wait End!");
				ThreadToWaitEnd.End += () =>
				{
					Console.WriteLine("Ended!");
					//throw(new Exception("aaaaaaaaaaaa"));
					WakeUpCallback();
				};
			}, HandleCallbacks: HandleCallbacks);

			return 0;
		}

		/// <summary>
		/// Wait until a thread has ended.
		/// </summary>
		/// <param name="ThreadId">Id of the thread to wait for.</param>
		/// <param name="Timeout">Timeout in microseconds (assumed).</param>
		/// <returns>Less than 0 on error</returns>
		[HlePspFunction(NID = 0x278C0DF5, FirmwareVersion = 150)]
		public int sceKernelWaitThreadEnd(int ThreadId, uint* Timeout)
		{
			return _sceKernelWaitThreadEndCB(ThreadId, Timeout, HandleCallbacks: false);
		}

		/// <summary>
		/// Wait until a thread has ended and handle callbacks if necessary.
		/// </summary>
		/// <param name="ThreadId">Id of the thread to wait for.</param>
		/// <param name="Timeout">Timeout in microseconds (assumed).</param>
		/// <returns>Less than 0 on error</returns>
		[HlePspFunction(NID = 0x840E8133, FirmwareVersion = 150)]
		public int sceKernelWaitThreadEndCB(int ThreadId, uint* Timeout)
		{
			return _sceKernelWaitThreadEndCB(ThreadId, Timeout, HandleCallbacks: true);
		}

		private int _sceKernelDelayThreadCB(uint DelayInMicroseconds, bool HandleCallbacks)
		{
			HleState.ThreadManager.Current.SetWaitAndPrepareWakeUp(HleThread.WaitType.Timer, "sceKernelDelayThread", WakeUpCallback =>
			{
				HleState.PspRtc.RegisterTimerInOnce(TimeSpanUtils.FromMicroseconds(DelayInMicroseconds), () =>
				{
					WakeUpCallback();
				});
			});

			return 0;
		}

		/// <summary>
		/// Delay the current thread by a specified number of microseconds
		/// </summary>
		/// <param name="DelayInMicroseconds">Delay in microseconds.</param>
		/// <example>
		///		sceKernelDelayThread(1000000); // Delay for a second
		/// </example>
		/// <returns></returns>
		[HlePspFunction(NID = 0xCEADEB47, FirmwareVersion = 150)]
		public int sceKernelDelayThread(uint DelayInMicroseconds)
		{
			return _sceKernelDelayThreadCB(DelayInMicroseconds, HandleCallbacks: false);
		}

		/// <summary>
		/// Delay the current thread by a specified number of microseconds and handle any callbacks.
		/// </summary>
		/// <param name="DelayInMicroseconds">Delay in microseconds.</param>
		/// <example>
		///		sceKernelDelayThread(1000000); // Delay for a second
		/// </example>
		/// <returns></returns>
		[HlePspFunction(NID = 0x68DA9E36, FirmwareVersion = 150)]
		public int sceKernelDelayThreadCB(uint DelayInMicroseconds)
		{
			return _sceKernelDelayThreadCB(DelayInMicroseconds, HandleCallbacks: true);
		}

		/// <summary>
		/// Modify the attributes of the current thread.
		/// </summary>
		/// <param name="unknown">Set to 0.</param>
		/// <param name="attr">The thread attributes to modify.  One of ::PspThreadAttributes.</param>
		/// <returns>Less than 0 on error</returns>
		[HlePspFunction(NID = 0xEA748E31, FirmwareVersion = 150)]
		[HlePspNotImplemented]
		public int sceKernelChangeCurrentThreadAttr(int Unknown, PspThreadAttributes Attributes)
		{
			return 0;
		}

		/// <summary>
		/// Change the threads current priority.
		/// </summary>
		/// <param name="ThreadId">The ID of the thread (from sceKernelCreateThread or sceKernelGetThreadId)</param>
		/// <param name="Priority">The new priority (the lower the number the higher the priority)</param>
		/// <example>
		///		int thid = sceKernelGetThreadId();
		///		// Change priority of current thread to 16
		///		sceKernelChangeThreadPriority(thid, 16);
		/// </example>
		/// <returns>0 if successful, otherwise the error code.</returns>
		[HlePspFunction(NID = 0x71BC9871, FirmwareVersion = 150)]
		[HlePspNotImplemented]
		public int sceKernelChangeThreadPriority(CpuThreadState CpuThreadState, int ThreadId, int Priority)
		{
			GetThreadById(ThreadId).PriorityValue = Priority;
			HleState.ThreadManager.Reschedule();
			CpuThreadState.Yield();
			//throw(new NotImplementedException());
			return 0;
		}

		/// <summary>
		/// Resume a thread previously put into a suspended state with ::sceKernelSuspendThread.
		/// </summary>
		/// <param name="ThreadId">UID of the thread to resume.</param>
		/// <returns>
		///		Success if greater or equalthan 0,
		///		an error if less than 0.
		/// </returns>
		[HlePspFunction(NID = 0x9944F31F, FirmwareVersion = 150)]
		public int sceKernelResumeThread(int ThreadId)
		{
			throw(new NotImplementedException());
			return -1;
		}

		/// <summary>
		/// Get the exit status of a thread.
		/// </summary>
		/// <param name="ThreadId">The UID of the thread to check.</param>
		/// <returns>The exit status</returns>
		[HlePspFunction(NID = 0x3B183E26, FirmwareVersion = 150)]
		public int sceKernelGetThreadExitStatus(int ThreadId)
		{
			var Thread = HleState.ThreadManager.GetThreadById(ThreadId);
			return Thread.Info.ExitStatus;
		}

		/// <summary>
		/// Exit a thread and delete itself.
		/// </summary>
		/// <param name="ExitStatus">Exit status</param>
		/// <returns></returns>
		[HlePspFunction(NID = 0x809CE29B, FirmwareVersion = 150)]
		public int sceKernelExitDeleteThread(int ExitStatus)
		{
			var CurrentThreadId = HleState.ThreadManager.Current.Id;
			int ResultExit = sceKernelExitThread(ExitStatus);
			int ResultDelete = sceKernelDeleteThread(CurrentThreadId);
			return ResultDelete;
		}

		/// <summary>
		/// Exit a thread
		/// </summary>
		/// <param name="ExitStatus">Exit status.</param>
		/// <returns></returns>
		[HlePspFunction(NID = 0xAA73C935, FirmwareVersion = 150)]
		public int sceKernelExitThread(int ExitStatus)
		{
			var Thread = HleState.ThreadManager.Current;
			Thread.Info.ExitStatus = ExitStatus;
			Thread.Exit();
			Thread.CpuThreadState.Yield();

			return 0;
		}

		/// <summary>
		/// Delete a thread
		/// </summary>
		/// <param name="ThreadId">UID of the thread to be deleted.</param>
		/// <returns>Less than 0 on error.</returns>
		[HlePspFunction(NID = 0x9FA03CD3, FirmwareVersion = 150)]
		public int sceKernelDeleteThread(int ThreadId)
		{
			var Thread = HleState.ThreadManager.GetThreadById(ThreadId);
			HleState.ThreadManager.DeleteThread(Thread);
			return 0;
			//return _sceKernelExitDeleteThread(-1, GetThreadById(ThreadId));
		}

		/// <summary>
		/// Terminate and delete a thread.
		/// </summary>
		/// <param name="ThreadId">UID of the thread to terminate and delete.</param>
		/// <returns>Success if greater or equal to 0, an error if less than 0.</returns>
		[HlePspFunction(NID = 0x383F7BCC, FirmwareVersion = 150)]
		public int sceKernelTerminateDeleteThread(int ThreadId)
		{
			return sceKernelDeleteThread(ThreadId);
			//throw(new NotImplementedException());

		}

		/// <summary>
		/// Get the current priority of the thread you are in.
		/// </summary>
		/// <returns>The current thread priority</returns>
		[HlePspFunction(NID = 0x94AA61EE, FirmwareVersion = 150)]
		public int sceKernelGetThreadCurrentPriority()
		{
			return HleState.ThreadManager.Current.Info.PriorityCurrent;
		}

		/*
		public int _sceKernelExitDeleteThread(int Status, HleThread Thread)
		{
			if (Thread != null)
			{
				Thread.Finalize();
				HleState.ThreadManager.Exit(Thread);
			}
			return 0;
		}
		*/
	}
}
