using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using NLog;

namespace SharpTrader.AlgoFramework
{
    public abstract partial class TradingAlgo : TraderBot
    {
        ConcurrentQueue<Command> Commands = new ConcurrentQueue<Command>();

 

        public Task<string> ForceCloseOperation(string id)
        {
            Task<string> callback()
            {

                Operation oper;
                lock (DbLock)
                    oper = this._ActiveOperations.FirstOrDefault(op => op.Id == id);
                if (oper != null && !oper.IsClosing)
                {
                    oper.ScheduleClose(Market.Time.AddSeconds(30));
                    return Task.FromResult($"Operation {oper} scheduled for close in 30 seconds");
                }
                else
                {
                    return Task.FromResult("Operation not found or already closing");
                }

            }
            var cmd = new Command<string>(callback);
            Commands.Enqueue(cmd);
            return cmd.GetResult();
        }

        public Task<string> RequestResumeOperation(string id)
        {
            Task<string> callback()
            {
                Operation oper;
                lock (DbLock)
                    oper = this.DbClosedOperations.FindById(id);

                if (oper != null && (oper.IsClosing || oper.IsClosed))
                {
                    this.ResumeOperation(oper);
                    return Task.FromResult($"Operation {oper} resumed");
                }
                else
                    return Task.FromResult($"Operation not found or already active");
            }

            var cmd = new Command<string>(callback);
            Commands.Enqueue(cmd);
            return cmd.GetResult();
        }

        public Task<string> ForceLiquidateOpetion(string id)
        {
            async Task<string> callback()
            {
                Operation op;
                lock (DbLock)
                    op = this._ActiveOperations.FirstOrDefault(o => o.Id == id);
                if (op != null)
                {
                    var lr = await TryLiquidateOperation(op, " user requested liquidation");
                    return $"LiquidationResult: amountRemainingLow {lr.amountRemainingLow} - order error {lr.OrderError}";
                }
                return "Operation not found";
            }

            var cmd = new Command<string>(callback);
            Commands.Enqueue(cmd);
            return cmd.GetResult();
        }

        public Task RequestCancelEntryOrders()
        {
            return this.Executor.CancelEntryOrders();
        }

        public async Task RequestStopEntries()
        {
            this.State.EntriesSuspendedByUser = true;
            await this.Executor.CancelEntryOrders();
            this.SaveNonVolatileVars();
        }

        public void RequestResumeEntries()
        {
            this.State.EntriesSuspendedByUser = false;
            this.SaveNonVolatileVars();
        }

    }
    abstract class Command
    {
        public abstract Task Run();
    }
    class Command<T> : Command
    {
        private volatile bool Executed = false;
        Func<Task<T>> Callback;
        TaskCompletionSource<T> CompletionSource = new TaskCompletionSource<T>();

        public Command(Func<Task<T>> callback)
        {
            Callback = callback;
            CompletionSource = new TaskCompletionSource<T>();
        }

        public override async Task Run()
        {
            if (!Executed)
            {
                Executed = true;
                var result = await Callback();
                CompletionSource.TrySetResult(result);
            }
        }

        public Task<T> GetResult() => CompletionSource.Task;
    }
}
