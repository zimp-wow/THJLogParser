using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EQLogParser
{
    public class ConcurrentQueue<T> : IDisposable
    {
        internal System.Collections.Concurrent.BlockingCollection<StrongBox<T>> InternalCollection;
        public ConcurrentQueue()
        {
            this.InternalCollection = new System.Collections.Concurrent.BlockingCollection<StrongBox<T>>(new System.Collections.Concurrent.ConcurrentQueue<StrongBox<T>>());
        }
        public ConcurrentQueue(System.Collections.Concurrent.ConcurrentQueue<StrongBox<T>> InitialCollection)
        {
            this.InternalCollection = new System.Collections.Concurrent.BlockingCollection<StrongBox<T>>(InitialCollection);
        }
        public void Enqueue(T O)
        {
            InternalCollection.Add(new StrongBox<T>(O));
        }

        public long Count
        {
            get
            {
                return InternalCollection.LongCount();
            }
        }
        public bool TryDequeue(ref T O)
        {
            StrongBox<T> sb = null;
            bool ret = InternalCollection.TryTake(out sb);
            if (ret)
                O = sb.Value;
            return ret;
        }

        public T DeQueue(long Timeout = -1)
        {
            return this.InternalCollection.Take().Value;
        }

        public bool IsEmpty()
        {
            return (this.InternalCollection.Count == 0);
        }

        public void WaitEmpty()
        {
            while (!this.IsEmpty())
                System.Threading.Thread.Sleep(1);
        }

        private bool disposedValue;
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                this.disposedValue = true;
                if (disposing)
                {
                    this?.InternalCollection?.Dispose();
                    this.InternalCollection = null;
                }
            }
        }
        public void Dispose()
        {
            Dispose(true);
        }
    }
}
