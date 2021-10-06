namespace System.Collections
{
	using System;
	using System.Runtime.Serialization;
	using System.Threading;
	using System.Runtime.CompilerServices;
	using System.Diagnostics.Contracts;

	internal static class HashHelpers
	{
		public static readonly int[] primes = {
			3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
			1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
			17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
			187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
			1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369};

		private static ConditionalWeakTable<object, SerializationInfo> s_SerializationInfoTable;

		internal static ConditionalWeakTable<object, SerializationInfo> SerializationInfoTable
		{
			get
			{
				if (s_SerializationInfoTable == null)
				{
					ConditionalWeakTable<object, SerializationInfo> newTable = new ConditionalWeakTable<object, SerializationInfo>();
					Interlocked.CompareExchange(ref s_SerializationInfoTable, newTable, null);
				}

				return s_SerializationInfoTable;
			}

		}

		public static bool IsPrime(int candidate)
		{
			if ((candidate & 1) != 0)
			{
				int limit = (int)Math.Sqrt(candidate);
				for (int divisor = 3; divisor <= limit; divisor += 2)
				{
					if ((candidate % divisor) == 0)
						return false;
				}
				return true;
			}
			return (candidate == 2);
		}

		public static int GetPrime(int min)
		{
			if (min < 0)
				throw new ArgumentException();

			for (int i = 0; i < primes.Length; i++)
			{
				int prime = primes[i];
				if (prime >= min) return prime;
			}

			//outside of our predefined table. 
			//compute the hard way. 
			for (int i = (min | 1); i < Int32.MaxValue; i += 2)
			{
				//if (IsPrime(i) && ((i - 1) % Hashtable.Prime != 0))
				if (IsPrime(i) && ((i - 1) % 12345 != 0))
					return i;
			}
			return min;
		}

		public static int GetMinPrime()
		{
			return primes[0];
		}

		public static int ExpandPrime(int oldSize)
		{
			int newSize = 2 * oldSize;

			// Allow the hashtables to grow to maximum possible size (~2G elements) before encoutering capacity overflow.
			// Note that this check works even when _items.Length overflowed thanks to the (uint) cast
			if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
			{
				Contract.Assert(MaxPrimeArrayLength == GetPrime(MaxPrimeArrayLength), "Invalid MaxPrimeArrayLength");
				return MaxPrimeArrayLength;
			}

			return GetPrime(newSize);
		}

		public const int MaxPrimeArrayLength = 0x7FEFFFFD;
	}

    [Serializable]
	internal class CompatibleComparer : IEqualityComparer
	{
		IComparer _comparer;
		IHashCodeProvider _hcp;

		internal CompatibleComparer(IComparer comparer, IHashCodeProvider hashCodeProvider)
		{
			_comparer = comparer;
			_hcp = hashCodeProvider;
		}
		public int Compare(Object a, Object b)
		{
			if (a == b) return 0;
			if (a == null) return -1;
			if (b == null) return 1;
			if (_comparer != null)
				return _comparer.Compare(a, b);
			IComparable ia = a as IComparable;
			if (ia != null)
				return ia.CompareTo(b);

			throw new ArgumentException();
		}

		public new bool Equals(Object a, Object b)
		{
			return Compare(a, b) == 0;
		}

		public int GetHashCode(Object obj)
		{
			if (obj == null)
			{
				throw new ArgumentNullException("obj");
			}
			Contract.EndContractBlock();

			if (_hcp != null)
				return _hcp.GetHashCode(obj);
			return obj.GetHashCode();
		}

		internal IComparer Comparer
		{
			get
			{
				return _comparer;
			}
		}

		internal IHashCodeProvider HashCodeProvider
		{
			get
			{
				return _hcp;
			}
		}
	}
}
