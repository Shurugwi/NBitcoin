﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NBitcoin
{
	public class Scope : IDisposable
	{
		Action close;
		public Scope(Action open, Action close)
		{
			this.close = close;
			open();
		}

		#region IDisposable Members

		public void Dispose()
		{
			close();
		}

		#endregion

		public static IDisposable Nothing
		{
			get
			{
				return new Scope(() =>
				{
				}, () =>
				{
				});
			}
		}
	}
	public partial class BitcoinStream
	{
		int _MaxArraySize = Int32.MaxValue;
		public int MaxArraySize
		{
			get
			{
				return _MaxArraySize;
			}
			set
			{
				_MaxArraySize = value;
			}
		}

		//ReadWrite<T>(ref T data)
		static MethodInfo _ReadWriteTyped;
		static BitcoinStream()
		{
			_ReadWriteTyped =
				typeof(BitcoinStream)
				.GetMethods()
				.Where(m => m.Name == "ReadWrite")
				.Where(m => m.IsGenericMethodDefinition)
				.Where(m => m.GetParameters().Length == 1)
				.Where(m => m.GetParameters().Any(p => p.ParameterType.IsByRef))
				.First();

		}

		private readonly Stream _Inner;
		public Stream Inner
		{
			get
			{
				return _Inner;
			}
		}

		private readonly bool _Serializing;
		public bool Serializing
		{
			get
			{
				return _Serializing;
			}
		}
		public BitcoinStream(Stream inner, bool serializing)
		{
			_Serializing = serializing;
			_Inner = inner;
		}

		public BitcoinStream(byte[] bytes)
			: this(new MemoryStream(bytes), false)
		{
		}

		public T ReadWrite<T>(T data) where T : IBitcoinSerializable
		{
			ReadWrite<T>(ref data);
			return data;
		}

		public void ReadWriteAsVarString(ref byte[] bytes)
		{
			VarString str = new VarString(bytes);
			ReadWrite(ref str);
			bytes = str.GetString(true);
		}

		public void ReadWrite(Type type, ref object obj)
		{
			try
			{
				var parameters = new object[] { obj };
				_ReadWriteTyped.MakeGenericMethod(type).Invoke(this, parameters);
				obj = parameters[0];
			}
			catch(TargetInvocationException ex)
			{
				throw ex.InnerException;
			}
		}

		public void ReadWrite(ref byte data)
		{
			ReadWriteByte(ref data);
		}
		public byte ReadWrite(byte data)
		{
			ReadWrite(ref data);
			return data;
		}

		public void ReadWrite(ref bool data)
		{
			byte d = data ? (byte)1 : (byte)0;
			ReadWriteByte(ref d);
			data = (d == 0 ? false : true);
		}


		public void ReadWrite<T>(ref T data) where T : IBitcoinSerializable
		{
			if(data == null)
				data = Activator.CreateInstance<T>();
			((IBitcoinSerializable)data).ReadWrite(this);
		}

		public void ReadWrite<T>(ref List<T> list) where T : IBitcoinSerializable, new()
		{
			ReadWriteList<T>(ref list);
		}
		public void ReadWrite(ref byte[] arr)
		{
			ReadWriteBytes(ref arr);
		}
		public void ReadWrite<T>(ref T[] arr) where T : IBitcoinSerializable, new()
		{
			ReadWriteArray<T>(ref arr);
		}

		private void ReadWriteNumber(ref long value, int size)
		{
			ulong uvalue = unchecked((ulong)value);
			ReadWriteNumber(ref uvalue, size);
			value = unchecked((long)uvalue);
		}

		private void ReadWriteNumber(ref ulong value, int size)
		{
			var bytes = new byte[size];

			for(int i = 0 ; i < size ; i++)
			{
				bytes[i] = (byte)(value >> i * 8);
			}
			if(IsBigEndian)
				Array.Reverse(bytes);
			ReadWriteBytes(ref bytes);
			if(IsBigEndian)
				Array.Reverse(bytes);
			ulong valueTemp = 0;
			for(int i = 0 ; i < bytes.Length ; i++)
			{
				var v = (ulong)bytes[i];
				valueTemp += v << (i * 8);
			}
			value = valueTemp;
		}

		private void ReadWriteBytes(ref byte[] data)
		{
			if(Serializing)
			{
				Inner.Write(data, 0, data.Length);
				Counter.AddWritten(data.Length);
			}
			else
			{
				var readen = Inner.ReadEx(data, 0, data.Length, ReadCancellationToken);
				if(readen == -1)
					throw new EndOfStreamException("No more byte to read");
				Counter.AddReaden(readen);

			}
		}
		private PerformanceCounter _Counter;
		public PerformanceCounter Counter
		{
			get
			{
				if(_Counter == null)
					_Counter = new PerformanceCounter();
				return _Counter;
			}
		}
		private void ReadWriteByte(ref byte data)
		{
			if(Serializing)
			{
				Inner.WriteByte(data);
				Counter.AddWritten(1);
			}
			else
			{
				data = (byte)Inner.ReadByte();
				Counter.AddReaden(1);
			}
		}

		public bool IsBigEndian
		{
			get;
			set;
		}

		public IDisposable BigEndianScope()
		{
			var old = IsBigEndian;
			return new Scope(() =>
			{
				IsBigEndian = true;
			},
			() =>
			{
				IsBigEndian = old;
			});
		}

		ProtocolVersion _ProtocolVersion = ProtocolVersion.PROTOCOL_VERSION;
		public ProtocolVersion ProtocolVersion
		{
			get
			{
				return _ProtocolVersion;
			}
			set
			{
				_ProtocolVersion = value;
			}
		}


		public IDisposable ProtocolVersionScope(ProtocolVersion version)
		{
			var old = ProtocolVersion;
			return new Scope(() =>
			{
				ProtocolVersion = version;
			},
			() =>
			{
				ProtocolVersion = old;
			});
		}

		public void CopyParameters(BitcoinStream stream)
		{
			ProtocolVersion = stream.ProtocolVersion;
			IsBigEndian = stream.IsBigEndian;
			MaxArraySize = stream.MaxArraySize;
		}

		private bool _NetworkFormat;
		public bool NetworkFormat
		{
			get
			{
				return _NetworkFormat;
			}
		}

		public IDisposable NetworkFormatScope(bool value)
		{
			var old = _NetworkFormat;
			return new Scope(() =>
			{
				_NetworkFormat = value;
			}, () =>
			{
				_NetworkFormat = old;
			});
		}

		public System.Threading.CancellationToken ReadCancellationToken
		{
			get;
			set;
		}

		public void ReadWriteAsVarInt(ref uint val)
		{
			ulong vallong = val;
			ReadWriteAsVarInt(ref vallong);
			if(!Serializing)
				val = (uint)vallong;
		}
		public void ReadWriteAsVarInt(ref ulong val)
		{
			var value = new VarInt(val);
			ReadWrite(ref value);
			if(!Serializing)
				val = value.ToLong();
		}

		public void ReadWriteAsCompactVarInt(ref uint val)
		{
			var value = new CompactVarInt(val, sizeof(uint));
			ReadWrite(ref value);
			if(!Serializing)
				val = (uint)value.ToLong();
		}
		public void ReadWriteAsCompactVarInt(ref ulong val)
		{
			var value = new CompactVarInt(val, sizeof(ulong));
			ReadWrite(ref value);
			if(!Serializing)
				val = value.ToLong();
		}
	}
}
