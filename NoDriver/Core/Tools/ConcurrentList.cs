using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace NoDriver.Core.Tools
{
    internal class ConcurrentList<T> : IList<T>
    {
        private readonly object _lock = new();
        private readonly List<T> _list = new();
        
        public T this[int index]
        {
            get 
            { 
                lock (_lock) 
                    return _list[index]; 
            }
            set 
            { 
                lock (_lock) 
                    _list[index] = value; 
            }
        }

        public int Count 
        { 
            get 
            { 
                lock (_lock) 
                    return _list.Count; 
            } 
        }

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            lock (_lock) 
                _list.Add(item);
        }

        public void Clear()
        {
            lock (_lock) 
                _list.Clear();
        }

        public bool Contains(T item)
        {
            lock (_lock) 
                return _list.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (_lock) 
                _list.CopyTo(array, arrayIndex);
        }

        public int IndexOf(T item)
        {
            lock (_lock) 
                return _list.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            lock (_lock) 
                _list.Insert(index, item);
        }

        public bool Remove(T item)
        {
            lock (_lock) 
                return _list.Remove(item);
        }

        public void RemoveAt(int index)
        {
            lock (_lock) 
                _list.RemoveAt(index);
        }

        public IEnumerator<T> GetEnumerator()
        {
            List<T> snapshot;
            lock (_lock)
                snapshot = new(_list);
            return snapshot.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public List<T> ToList()
        {
            lock (_lock)
                return new(_list);
        }

        public T? FirstOrDefault(Func<T, bool> predicate)
        {
            lock (_lock)
                return _list.FirstOrDefault(predicate);
        }

        public List<T> Where(Func<T, bool> predicate)
        {
            lock (_lock) 
                return _list.Where(predicate).ToList();
        }

        public bool AddIfNotExist(Func<T, bool> predicate, Func<T> createFunc)
        {
            lock (_lock)
            {
                if (_list.Any(predicate))
                    return false;
                _list.Add(createFunc());
                return true;
            }
        }
    }
}
