namespace System.Collections.Generic
{
	using System;
	using System.Collections;
	using System.Diagnostics;
	using System.Runtime.Serialization;

	[DebuggerDisplay("Count = {Count}")]
	[Serializable]
	[Runtime.InteropServices.ComVisible(false)]
	public class Dictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue>, ISerializable, IDeserializationCallback
	{
		private struct Entry
		{
			public int hashCode;    // Lower 31 bits of hash code, -1 if unused
			public int next;        // Index of next entry, -1 if last
			public TKey key;           // Key of entry
			public TValue value;         // Value of entry
		}

		private int[] buckets;
		private Entry[] entries;
		private int count;
		private int version;
		private int freeList;
		private int freeCount;
		private IEqualityComparer<TKey> comparer;
		private KeyCollection keys;
		private ValueCollection values;
		private Object _syncRoot;

		// constants for serialization
		private const String VersionName = "Version";
		private const String HashSizeName = "HashSize";  // Must save buckets.Length
		private const String KeyValuePairsName = "KeyValuePairs";
		private const String ComparerName = "Comparer";

		public Dictionary() : this(0, null) { }

		public Dictionary(int capacity) : this(capacity, null) { }

		public Dictionary(IEqualityComparer<TKey> comparer) : this(0, comparer) { }

		public Dictionary(int capacity, IEqualityComparer<TKey> comparer)
		{
			if (capacity < 0) throw new ArgumentOutOfRangeException();
			if (capacity > 0) Initialize(capacity);
			this.comparer = comparer ?? EqualityComparer<TKey>.Default;
		}

		public Dictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }

		public Dictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer) :
			this(dictionary != null ? dictionary.Count : 0, comparer)
		{

			if (dictionary == null)
			{
				throw new ArgumentNullException();
			}

			foreach (KeyValuePair<TKey, TValue> pair in dictionary)
			{
				Add(pair.Key, pair.Value);
			}
		}

		protected Dictionary(SerializationInfo info, StreamingContext context)
		{
			//We can't do anything with the keys and values until the entire graph has been deserialized
			//and we have a resonable estimate that GetHashCode is not going to fail.  For the time being,
			//we'll just cache this.  The graph is not valid until OnDeserialization has been called.
			HashHelpers.SerializationInfoTable.Add(this, info);
		}

		public IEqualityComparer<TKey> Comparer
		{
			get
			{
				return comparer;
			}
		}

		public int Count
		{
			get { return count - freeCount; }
		}

		public KeyCollection Keys
		{
			get
			{
				if (keys == null) keys = new KeyCollection(this);
				return keys;
			}
		}

		ICollection<TKey> IDictionary<TKey, TValue>.Keys
		{
			get
			{
				if (keys == null) keys = new KeyCollection(this);
				return keys;
			}
		}

		IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys
		{
			get
			{
				if (keys == null) keys = new KeyCollection(this);
				return keys;
			}
		}

		public ValueCollection Values
		{
			get
			{
				if (values == null) values = new ValueCollection(this);
				return values;
			}
		}

		ICollection<TValue> IDictionary<TKey, TValue>.Values
		{
			get
			{
				if (values == null) values = new ValueCollection(this);
				return values;
			}
		}

		IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values
		{
			get
			{
				if (values == null) values = new ValueCollection(this);
				return values;
			}
		}

		public TValue this[TKey key]
		{
			get
			{
				int i = FindEntry(key);
				if (i >= 0) return entries[i].value;
				throw new KeyNotFoundException();
				return default(TValue);
			}
			set
			{
				Insert(key, value, false);
			}
		}

		public void Add(TKey key, TValue value)
		{
			Insert(key, value, true);
		}

		void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair)
		{
			Add(keyValuePair.Key, keyValuePair.Value);
		}

		bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair)
		{
			int i = FindEntry(keyValuePair.Key);
			if (i >= 0 && EqualityComparer<TValue>.Default.Equals(entries[i].value, keyValuePair.Value))
			{
				return true;
			}
			return false;
		}

		bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
		{
			int i = FindEntry(keyValuePair.Key);
			if (i >= 0 && EqualityComparer<TValue>.Default.Equals(entries[i].value, keyValuePair.Value))
			{
				Remove(keyValuePair.Key);
				return true;
			}
			return false;
		}

		public void Clear()
		{
			if (count > 0)
			{
				for (int i = 0; i < buckets.Length; i++) buckets[i] = -1;
				Array.Clear(entries, 0, count);
				freeList = -1;
				count = 0;
				freeCount = 0;
				version++;
			}
		}

		public bool ContainsKey(TKey key)
		{
			return FindEntry(key) >= 0;
		}

		public bool ContainsValue(TValue value)
		{
			if (value == null)
			{
				for (int i = 0; i < count; i++)
				{
					if (entries[i].hashCode >= 0 && entries[i].value == null) return true;
				}
			}
			else
			{
				EqualityComparer<TValue> c = EqualityComparer<TValue>.Default;
				for (int i = 0; i < count; i++)
				{
					if (entries[i].hashCode >= 0 && c.Equals(entries[i].value, value)) return true;
				}
			}
			return false;
		}

		private void CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
		{
			if (array == null)
			{
				throw new ArgumentNullException();
			}

			if (index < 0 || index > array.Length)
			{
				throw new ArgumentOutOfRangeException();
			}

			if (array.Length - index < Count)
			{
				throw new ArgumentException();
			}

			int count = this.count;
			Entry[] entries = this.entries;
			for (int i = 0; i < count; i++)
			{
				if (entries[i].hashCode >= 0)
				{
					array[index++] = new KeyValuePair<TKey, TValue>(entries[i].key, entries[i].value);
				}
			}
		}

		public Enumerator GetEnumerator()
		{
			return new Enumerator(this, Enumerator.KeyValuePair);
		}

		IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
		{
			return new Enumerator(this, Enumerator.KeyValuePair);
		}

		[Security.SecurityCritical]  // auto-generated_required
		public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			if (info == null)
			{
				throw new ArgumentNullException();
			}
			info.AddValue(VersionName, version);

			info.AddValue(HashSizeName, buckets == null ? 0 : buckets.Length); //This is the length of the bucket array.
			if (buckets != null)
			{
				KeyValuePair<TKey, TValue>[] array = new KeyValuePair<TKey, TValue>[Count];
				CopyTo(array, 0);
				info.AddValue(KeyValuePairsName, array, typeof(KeyValuePair<TKey, TValue>[]));
			}
		}

		private int FindEntry(TKey key)
		{
			if (key == null)
			{
				throw new ArgumentNullException();
			}

			if (buckets != null)
			{
				int hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;
				for (int i = buckets[hashCode % buckets.Length]; i >= 0; i = entries[i].next)
				{
					if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key)) return i;
				}
			}
			return -1;
		}

		private void Initialize(int capacity)
		{
			int size = HashHelpers.GetPrime(capacity);
			buckets = new int[size];
			for (int i = 0; i < buckets.Length; i++) buckets[i] = -1;
			entries = new Entry[size];
			freeList = -1;
		}

		private void Insert(TKey key, TValue value, bool add)
		{

			if (key == null)
			{
				throw new ArgumentNullException();
			}

			if (buckets == null) Initialize(0);

			int hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;

			//Determines in which index in the bucket array an item will be 
			int targetBucket = hashCode % buckets.Length;

			//i = selected bucket's value
			//if it's not -1 , we enter this loop
			for (int i = buckets[targetBucket]; i >= 0; i = entries[i].next)
			{
				// if new added item's and the appropriate entry's hash codes match
				//and if their keys match
				if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key))
				{
					if (add)
					{
						//an item already exists with that key
						throw new ArgumentException();
					}
					//if we are not adding the item, we just change the value then
					entries[i].value = value;
					version++;
					return;
				}
			}
			//
			int index;
			if (freeCount > 0)
			{
				index = freeList;
				freeList = entries[index].next;
				freeCount--;
			}
			else
			{
				if (count == entries.Length)
				{
					Resize();
					targetBucket = hashCode % buckets.Length;
				}
				index = count;
				count++;
			}

			entries[index].hashCode = hashCode;
			entries[index].next = buckets[targetBucket];
			entries[index].key = key;
			entries[index].value = value;
			buckets[targetBucket] = index;
			version++;
		}
		
		public virtual void OnDeserialization(Object sender)
		{
			SerializationInfo siInfo;
			HashHelpers.SerializationInfoTable.TryGetValue(this, out siInfo);

			if (siInfo == null)
			{
				// It might be necessary to call OnDeserialization from a container if the container object also implements
				// OnDeserialization. However, remoting will call OnDeserialization again.
				// We can return immediately if this function is called twice. 
				// Note we set remove the serialization info from the table at the end of this method.
				return;
			}

			int realVersion = siInfo.GetInt32(VersionName);
			int hashsize = siInfo.GetInt32(HashSizeName);
			comparer = (IEqualityComparer<TKey>)siInfo.GetValue(ComparerName, typeof(IEqualityComparer<TKey>));

			if (hashsize != 0)
			{
				buckets = new int[hashsize];
				for (int i = 0; i < buckets.Length; i++) buckets[i] = -1;
				entries = new Entry[hashsize];
				freeList = -1;

				KeyValuePair<TKey, TValue>[] array = (KeyValuePair<TKey, TValue>[])
					siInfo.GetValue(KeyValuePairsName, typeof(KeyValuePair<TKey, TValue>[]));

				if (array == null)
				{
					throw new InvalidOperationException();
				}

				for (int i = 0; i < array.Length; i++)
				{
					if (array[i].Key == null)
					{
						throw new SerializationException();
					}
					Insert(array[i].Key, array[i].Value, true);
				}
			}
			else
			{
				buckets = null;
			}

			version = realVersion;
			HashHelpers.SerializationInfoTable.Remove(this);
		}

		private void Resize()
		{
			Resize(HashHelpers.ExpandPrime(count), false);
		}

		private void Resize(int newSize, bool forceNewHashCodes)
		{
			int[] newBuckets = new int[newSize];
			for (int i = 0; i < newBuckets.Length; i++) newBuckets[i] = -1;
			Entry[] newEntries = new Entry[newSize];
			Array.Copy(entries, 0, newEntries, 0, count);
			if (forceNewHashCodes)
			{
				for (int i = 0; i < count; i++)
				{
					if (newEntries[i].hashCode != -1)
					{
						newEntries[i].hashCode = (comparer.GetHashCode(newEntries[i].key) & 0x7FFFFFFF);
					}
				}
			}
			for (int i = 0; i < count; i++)
			{
				if (newEntries[i].hashCode >= 0)
				{
					int bucket = newEntries[i].hashCode % newSize;
					newEntries[i].next = newBuckets[bucket];
					newBuckets[bucket] = i;
				}
			}
			buckets = newBuckets;
			entries = newEntries;
		}

		public bool Remove(TKey key)
		{
			if (key == null)
			{
				throw new ArgumentNullException();
			}

			if (buckets != null)
			{
				int hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;
				int bucket = hashCode % buckets.Length;
				int last = -1;
				for (int i = buckets[bucket]; i >= 0; last = i, i = entries[i].next)
				{
					if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key))
					{
						if (last < 0)
						{
							buckets[bucket] = entries[i].next;
						}
						else
						{
							entries[last].next = entries[i].next;
						}
						entries[i].hashCode = -1;
						entries[i].next = freeList;
						entries[i].key = default(TKey);
						entries[i].value = default(TValue);
						freeList = i;
						freeCount++;
						version++;
						return true;
					}
				}
			}
			return false;
		}

		public bool TryGetValue(TKey key, out TValue value)
		{
			int i = FindEntry(key);
			if (i >= 0)
			{
				value = entries[i].value;
				return true;
			}
			value = default(TValue);
			return false;
		}

		// This is a convenience method for the internal callers that were converted from using Hashtable.
		// Many were combining key doesn't exist and key exists but null value (for non-value types) checks.
		// This allows them to continue getting that behavior with minimal code delta. This is basically
		// TryGetValue without the out param
		internal TValue GetValueOrDefault(TKey key)
		{
			int i = FindEntry(key);
			if (i >= 0)
			{
				return entries[i].value;
			}
			return default(TValue);
		}

		bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
		{
			get { return false; }
		}

		void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
		{
			CopyTo(array, index);
		}

		void ICollection.CopyTo(Array array, int index)
		{
			if (array == null)
			{
				throw new ArgumentNullException();
			}

			if (array.Rank != 1)
			{
				throw new ArgumentException();
			}

			if (array.GetLowerBound(0) != 0)
			{
				throw new ArgumentException();
			}

			if (index < 0 || index > array.Length)
			{
				throw new ArgumentOutOfRangeException();
			}

			if (array.Length - index < Count)
			{
				throw new ArgumentException();
			}

			KeyValuePair<TKey, TValue>[] pairs = array as KeyValuePair<TKey, TValue>[];
			if (pairs != null)
			{
				CopyTo(pairs, index);
			}
			else if (array is DictionaryEntry[])
			{
				DictionaryEntry[] dictEntryArray = array as DictionaryEntry[];
				Entry[] entries = this.entries;
				for (int i = 0; i < count; i++)
				{
					if (entries[i].hashCode >= 0)
					{
						dictEntryArray[index++] = new DictionaryEntry(entries[i].key, entries[i].value);
					}
				}
			}
			else
			{
				object[] objects = array as object[];
				if (objects == null)
				{
					throw new ArgumentException();
				}

				try
				{
					int count = this.count;
					Entry[] entries = this.entries;
					for (int i = 0; i < count; i++)
					{
						if (entries[i].hashCode >= 0)
						{
							objects[index++] = new KeyValuePair<TKey, TValue>(entries[i].key, entries[i].value);
						}
					}
				}
				catch (ArrayTypeMismatchException)
				{
					throw new ArgumentException();
				}
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return new Enumerator(this, Enumerator.KeyValuePair);
		}

		bool ICollection.IsSynchronized
		{
			get { return false; }
		}

		object ICollection.SyncRoot
		{
			get
			{
				if (_syncRoot == null)
				{
					System.Threading.Interlocked.CompareExchange<Object>(ref _syncRoot, new Object(), null);
				}
				return _syncRoot;
			}
		}

		bool IDictionary.IsFixedSize
		{
			get { return false; }
		}

		bool IDictionary.IsReadOnly
		{
			get { return false; }
		}

		ICollection IDictionary.Keys
		{
			get { return (ICollection)Keys; }
		}

		ICollection IDictionary.Values
		{
			get { return (ICollection)Values; }
		}

		object IDictionary.this[object key]
		{
			get
			{
				if (IsCompatibleKey(key))
				{
					int i = FindEntry((TKey)key);
					if (i >= 0)
					{
						return entries[i].value;
					}
				}
				return null;
			}
			set
			{
				if (key == null)
				{
					throw new ArgumentNullException();
				}

				try
				{
					TKey tempKey = (TKey)key;
					try
					{
						this[tempKey] = (TValue)value;
					}
					catch (InvalidCastException)
					{
						throw new InvalidCastException();
					}
				}
				catch (InvalidCastException)
				{
					throw new InvalidCastException();
				}
			}
		}

		private static bool IsCompatibleKey(object key)
		{
			if (key == null)
			{
				throw new ArgumentNullException();
			}
			return (key is TKey);
		}

		void IDictionary.Add(object key, object value)
		{
			if (key == null)
			{
				throw new ArgumentNullException();
			}

			try
			{
				TKey tempKey = (TKey)key;

				try
				{
					Add(tempKey, (TValue)value);
				}
				catch (InvalidCastException)
				{
					throw new InvalidCastException();
				}
			}
			catch (InvalidCastException)
			{
				throw new InvalidCastException();
			}
		}

		bool IDictionary.Contains(object key)
		{
			if (IsCompatibleKey(key))
			{
				return ContainsKey((TKey)key);
			}

			return false;
		}

		IDictionaryEnumerator IDictionary.GetEnumerator()
		{
			return new Enumerator(this, Enumerator.DictEntry);
		}

		void IDictionary.Remove(object key)
		{
			if (IsCompatibleKey(key))
			{
				Remove((TKey)key);
			}
		}

		[Serializable]
		public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>,
			IDictionaryEnumerator
		{
			private Dictionary<TKey, TValue> dictionary;
			private int version;
			private int index;
			private KeyValuePair<TKey, TValue> current;
			private int getEnumeratorRetType;  // What should Enumerator.Current return?

			internal const int DictEntry = 1;
			internal const int KeyValuePair = 2;

			internal Enumerator(Dictionary<TKey, TValue> dictionary, int getEnumeratorRetType)
			{
				this.dictionary = dictionary;
				version = dictionary.version;
				index = 0;
				this.getEnumeratorRetType = getEnumeratorRetType;
				current = new KeyValuePair<TKey, TValue>();
			}

			public bool MoveNext()
			{
				if (version != dictionary.version)
				{
					throw new InvalidOperationException();
				}

				// Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
				// dictionary.count+1 could be negative if dictionary.count is Int32.MaxValue
				while ((uint)index < (uint)dictionary.count)
				{
					if (dictionary.entries[index].hashCode >= 0)
					{
						current = new KeyValuePair<TKey, TValue>(dictionary.entries[index].key, dictionary.entries[index].value);
						index++;
						return true;
					}
					index++;
				}

				index = dictionary.count + 1;
				current = new KeyValuePair<TKey, TValue>();
				return false;
			}

			public KeyValuePair<TKey, TValue> Current
			{
				get { return current; }
			}

			public void Dispose()
			{
			}

			object IEnumerator.Current
			{
				get
				{
					if (index == 0 || (index == dictionary.count + 1))
					{
						throw new InvalidOperationException();
					}

					if (getEnumeratorRetType == DictEntry)
					{
						return new DictionaryEntry(current.Key, current.Value);
					}
					else
					{
						return new KeyValuePair<TKey, TValue>(current.Key, current.Value);
					}
				}
			}

			void IEnumerator.Reset()
			{
				if (version != dictionary.version)
				{
					throw new InvalidOperationException();
				}

				index = 0;
				current = new KeyValuePair<TKey, TValue>();
			}

			DictionaryEntry IDictionaryEnumerator.Entry
			{
				get
				{
					if (index == 0 || (index == dictionary.count + 1))
					{
						throw new InvalidOperationException();
					}

					return new DictionaryEntry(current.Key, current.Value);
				}
			}

			object IDictionaryEnumerator.Key
			{
				get
				{
					if (index == 0 || (index == dictionary.count + 1))
					{
						throw new InvalidOperationException();
					}

					return current.Key;
				}
			}

			object IDictionaryEnumerator.Value
			{
				get
				{
					if (index == 0 || (index == dictionary.count + 1))
					{
						throw new InvalidOperationException();
					}

					return current.Value;
				}
			}
		}

		[DebuggerDisplay("Count = {Count}")]
		[Serializable]
		public sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
		{
			private Dictionary<TKey, TValue> dictionary;

			public KeyCollection(Dictionary<TKey, TValue> dictionary)
			{
				if (dictionary == null)
				{
					throw new ArgumentNullException(nameof(dictionary));
				}
				this.dictionary = dictionary;
			}

			public Enumerator GetEnumerator()
			{
				return new Enumerator(dictionary);
			}

			public void CopyTo(TKey[] array, int index)
			{
				if (array == null)
				{
					throw new ArgumentNullException(nameof(array));
				}
				if (index < 0 || index > array.Length)
				{
					throw new IndexOutOfRangeException("Index was out of range.");
				}
				if (array.Length - index < dictionary.Count)
				{
					throw new ArgumentException();
				}

				int count = dictionary.count;
				Entry[] entries = dictionary.entries;
				for (int i = 0; i < count; i++)
				{
					if (entries[i].hashCode >= 0) array[index++] = entries[i].key;
				}
			}

			public int Count
			{
				get { return dictionary.Count; }
			}

			bool ICollection<TKey>.IsReadOnly
			{
				get { return true; }
			}

			void ICollection<TKey>.Add(TKey item)
			{
				throw new NotSupportedException();
			}

			void ICollection<TKey>.Clear()
			{
				throw new NotSupportedException();
			}

			bool ICollection<TKey>.Contains(TKey item)
			{
				return dictionary.ContainsKey(item);
			}

			bool ICollection<TKey>.Remove(TKey item)
			{
				throw new NotSupportedException();
				return false;
			}

			IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()
			{
				return new Enumerator(dictionary);
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return new Enumerator(dictionary);
			}

			void ICollection.CopyTo(Array array, int index)
			{
				if (array == null)
				{
					throw new ArgumentNullException(nameof(array));
				}
				if (array.Rank != 1)
				{
					throw new ArgumentException();
				}
				if (array.GetLowerBound(0) != 0)
				{
					throw new ArgumentException();
				}
				if (index < 0 || index > array.Length)
				{
					throw new ArgumentOutOfRangeException();
				}
				if (array.Length - index < dictionary.Count)
				{
					throw new ArgumentException();
				}

				TKey[] keys = array as TKey[];
				if (keys != null)
				{
					CopyTo(keys, index);
				}
				else
				{
					object[] objects = array as object[];
					if (objects == null)
					{
						throw new ArgumentException();
					}

					int count = dictionary.count;
					Entry[] entries = dictionary.entries;
					try
					{
						for (int i = 0; i < count; i++)
						{
							if (entries[i].hashCode >= 0) objects[index++] = entries[i].key;
						}
					}
					catch (ArrayTypeMismatchException)
					{
						throw new ArgumentException();
					}
				}
			}

			bool ICollection.IsSynchronized
			{
				get { return false; }
			}

			Object ICollection.SyncRoot
			{
				get { return ((ICollection)dictionary).SyncRoot; }
			}

			[Serializable]
			public struct Enumerator : IEnumerator<TKey>, System.Collections.IEnumerator
			{
				private Dictionary<TKey, TValue> dictionary;
				private int index;
				private int version;
				private TKey currentKey;

				internal Enumerator(Dictionary<TKey, TValue> dictionary)
				{
					this.dictionary = dictionary;
					version = dictionary.version;
					index = 0;
					currentKey = default(TKey);
				}

				public void Dispose()
				{
				}

				public bool MoveNext()
				{
					if (version != dictionary.version)
					{
						throw new InvalidOperationException();
					}

					while ((uint)index < (uint)dictionary.count)
					{
						if (dictionary.entries[index].hashCode >= 0)
						{
							currentKey = dictionary.entries[index].key;
							index++;
							return true;
						}
						index++;
					}

					index = dictionary.count + 1;
					currentKey = default(TKey);
					return false;
				}

				public TKey Current
				{
					get
					{
						return currentKey;
					}
				}

				Object System.Collections.IEnumerator.Current
				{
					get
					{
						if (index == 0 || (index == dictionary.count + 1))
						{
							throw new InvalidOperationException();
						}

						return currentKey;
					}
				}

				void System.Collections.IEnumerator.Reset()
				{
					if (version != dictionary.version)
					{
						throw new InvalidOperationException();
					}

					index = 0;
					currentKey = default(TKey);
				}
			}
		}

		[DebuggerDisplay("Count = {Count}")]
		[Serializable]
		public sealed class ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
		{
			private Dictionary<TKey, TValue> dictionary;

			public ValueCollection(Dictionary<TKey, TValue> dictionary)
			{
				if (dictionary == null)
				{
					throw new ArgumentNullException();
				}
				this.dictionary = dictionary;
			}

			public Enumerator GetEnumerator()
			{
				return new Enumerator(dictionary);
			}

			public void CopyTo(TValue[] array, int index)
			{
				if (array == null)
				{
					throw new ArgumentNullException();
				}

				if (index < 0 || index > array.Length)
				{
					throw new ArgumentOutOfRangeException();
				}

				if (array.Length - index < dictionary.Count)
				{
					throw new ArgumentException();
				}

				int count = dictionary.count;
				Entry[] entries = dictionary.entries;
				for (int i = 0; i < count; i++)
				{
					if (entries[i].hashCode >= 0) array[index++] = entries[i].value;
				}
			}

			public int Count
			{
				get { return dictionary.Count; }
			}

			bool ICollection<TValue>.IsReadOnly
			{
				get { return true; }
			}

			void ICollection<TValue>.Add(TValue item)
			{
				throw new NotSupportedException();
			}

			bool ICollection<TValue>.Remove(TValue item)
			{
				throw new NotSupportedException();
				return false;
			}

			void ICollection<TValue>.Clear()
			{
				throw new NotSupportedException();
			}

			bool ICollection<TValue>.Contains(TValue item)
			{
				return dictionary.ContainsValue(item);
			}

			IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
			{
				return new Enumerator(dictionary);
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return new Enumerator(dictionary);
			}

			void ICollection.CopyTo(Array array, int index)
			{
				if (array == null)
				{
					throw new ArgumentNullException();
				}

				if (array.Rank != 1)
				{
					throw new ArgumentException();
				}

				if (array.GetLowerBound(0) != 0)
				{
					throw new ArgumentException();
				}

				if (index < 0 || index > array.Length)
				{
					throw new ArgumentOutOfRangeException();
				}

				if (array.Length - index < dictionary.Count)
					throw new ArgumentException();

				TValue[] values = array as TValue[];
				if (values != null)
				{
					CopyTo(values, index);
				}
				else
				{
					object[] objects = array as object[];
					if (objects == null)
					{
						throw new ArgumentException();
					}

					int count = dictionary.count;
					Entry[] entries = dictionary.entries;
					try
					{
						for (int i = 0; i < count; i++)
						{
							if (entries[i].hashCode >= 0) objects[index++] = entries[i].value;
						}
					}
					catch (ArrayTypeMismatchException)
					{
						throw new ArgumentException();
					}
				}
			}

			bool ICollection.IsSynchronized
			{
				get { return false; }
			}

			Object ICollection.SyncRoot
			{
				get { return ((ICollection)dictionary).SyncRoot; }
			}

			[Serializable]
			public struct Enumerator : IEnumerator<TValue>, System.Collections.IEnumerator
			{
				private Dictionary<TKey, TValue> dictionary;
				private int index;
				private int version;
				private TValue currentValue;

				internal Enumerator(Dictionary<TKey, TValue> dictionary)
				{
					this.dictionary = dictionary;
					version = dictionary.version;
					index = 0;
					currentValue = default(TValue);
				}

				public void Dispose()
				{
				}

				public bool MoveNext()
				{
					if (version != dictionary.version)
					{
						throw new InvalidOperationException();
					}

					while ((uint)index < (uint)dictionary.count)
					{
						if (dictionary.entries[index].hashCode >= 0)
						{
							currentValue = dictionary.entries[index].value;
							index++;
							return true;
						}
						index++;
					}
					index = dictionary.count + 1;
					currentValue = default(TValue);
					return false;
				}

				public TValue Current
				{
					get
					{
						return currentValue;
					}
				}

				Object System.Collections.IEnumerator.Current
				{
					get
					{
						if (index == 0 || (index == dictionary.count + 1))
						{
							throw new InvalidOperationException();
						}

						return currentValue;
					}
				}

				void System.Collections.IEnumerator.Reset()
				{
					if (version != dictionary.version)
					{
						throw new InvalidOperationException();
					}
					index = 0;
					currentValue = default(TValue);
				}
			}
		}
	}
}