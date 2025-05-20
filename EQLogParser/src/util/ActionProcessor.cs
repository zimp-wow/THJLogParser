using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EQLogParser
{
  class ActionProcessor
  {
    public delegate void ProcessActionCallback(LineData data);
    private ConcurrentQueue<LineData> Queue = null;
    ManualResetEventSlim StopFlag = new ManualResetEventSlim(false);
    private readonly ProcessActionCallback callback;
    private bool Stopped = false;
    private readonly int DelayTime = 10;
    private long LinesAdded = 0;
    private long LinesProcessed = 0;

    public ActionProcessor(ProcessActionCallback callback)
    {
      this.Queue = new ConcurrentQueue<LineData>();
      this.callback = callback;
      Thread t = new Thread(() => Process());
      t.IsBackground = true;
      t.Start();
    }

    public void Add(LineData data)
    {
        Queue.Enqueue(data);
        LinesAdded++;
    }

    public long Size()
    {
      return Queue.Count;
    }

    public void Stop()
    {
      StopFlag.Reset();
      Stopped = true;
      StopFlag.Wait();
      this.Queue.Dispose();
    }

    public double GetPercentComplete()
    {
      return LinesAdded > 0 ? Math.Round((double)LinesProcessed / LinesAdded * 100, 1) : 100.0;
    }

    private void Process()
    {
      while (!Stopped)
      {
        LineData item = null;
        if (Queue.TryDequeue(ref item)){
            callback(item);
            LinesProcessed++;
        }
        else
        {
            Thread.Sleep(DelayTime);
        }
        StopFlag.Set();
      }
    }
  }
}
