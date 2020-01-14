﻿using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace LibOrbisPkg.Util
{
  static class MemoryReaderExtensions
  {
    public static void Read<T>(this IMemoryReader reader, long pos, out T value) where T : struct
    {
      int sizeOfT = Marshal.SizeOf(typeof(T));
      var buf = new byte[sizeOfT];
      reader.Read(pos, buf, 0, sizeOfT);
      value = ByteArrayToStructure<T>(buf, 0);
    }

    public static void ReadArray<T>(this IMemoryReader reader, long pos, T[] value, int offset, int count) where T : struct
    {
      // Fast path for bytes
      if (value is byte[] b)
      {
        reader.Read(pos, b, offset, count);
        return;
      }
      // Slow path
      int sizeOfT = Marshal.SizeOf(typeof(T));
      var buf = new byte[sizeOfT * count];
      reader.Read(pos, buf, 0, sizeOfT * count);
      for (var i = 0; i < count; i++)
      {
        value[i] = ByteArrayToStructure<T>(buf, i * sizeOfT);
      }
    }

    // https://stackoverflow.com/a/2887
    private static unsafe T ByteArrayToStructure<T>(byte[] bytes, int offset) where T : struct
    {
      fixed (byte* ptr = &bytes[offset])
      {
        return (T)Marshal.PtrToStructure((IntPtr)ptr, typeof(T));
      }
    }
  }
  public interface IMemoryAccessor : IMemoryReader
  {
    void Read<T>(long pos, out T value) where T : struct;
    void ReadArray<T>(long pos, T[] value, int offset, int count) where T : struct;
  }
  public interface IMemoryReader : IDisposable
  {
    void Read(long pos, byte[] buf, int offset, int count);
  }
  class MemoryAccessor : IMemoryAccessor
  {
    private IMemoryReader reader;
    private long offset;
    public MemoryAccessor(IMemoryReader mr, long offset = 0)
    {
      // Efficient nesting of MemoryAccessors: eliminating long call chains to the base IMemoryReader
      if(mr is MemoryAccessor ma)
      {
        this.offset = offset + ma.offset;
        this.reader = ma.reader;
      }
      else
      {
        this.offset = offset;
        this.reader = mr;
      }
    }
    public void Dispose() { }
    public void Read<T>(long pos, out T value) where T : struct
    {
      reader.Read(pos + offset, out value);
    }
    public void Read(long pos, byte[] buf, int offset, int count)
    {
      reader.Read(pos + this.offset, buf, offset, count);
    }
    public void ReadArray<T>(long pos, T[] value, int offset, int count) where T : struct
    {
      reader.ReadArray(pos + this.offset, value, offset, count);
    }
  }
  class ChunkedMemoryReader : IMemoryReader
  {
    private int chunkSize;
    private int[] chunks;
    private IMemoryReader reader;
    public ChunkedMemoryReader(IMemoryReader mr, int chunkSize, int[] chunks)
    {
      this.chunks = chunks;
      this.chunkSize = chunkSize;
      this.reader = mr;
    }

    public void Dispose()
    {
    }

    public void Read(long pos, byte[] buf, int offset, int count)
    {
      var chunkIdx = pos / chunkSize;
      while(count > 0)
      {
        int offsetIntoChunk = (int)(pos % chunkSize);
        int toReadFromChunk = Math.Min(chunkSize - offsetIntoChunk, count);
        reader.Read((long)chunks[chunkIdx++]*chunkSize + offsetIntoChunk, buf, offset, count);
        pos += toReadFromChunk;
        offset += toReadFromChunk;
        count -= toReadFromChunk;
      }
    }
  }
  public class MemoryMappedViewAccessor_ : IMemoryAccessor
  {
    private MemoryMappedViewAccessor _va;
    private bool shouldDispose;
    public MemoryMappedViewAccessor_(MemoryMappedViewAccessor v, bool shouldDispose = false)
    {
      _va = v;
      this.shouldDispose = shouldDispose;
    }

    public void Dispose()
    {
      if(shouldDispose)
        _va.Dispose();
    }

    public void Read<T>(long pos, out T value) where T : struct
    {
      _va.Read(pos, out value);
    }

    public void ReadArray<T>(long pos, T[] value, int offset, int count) where T : struct
    {
      _va.ReadArray(pos, value, offset, count);
    }

    public void Read(long pos, byte[] buf, int offset, int count)
    {
      _va.ReadArray(pos, buf, offset, count);
    }
  }

  public class StreamReader : IMemoryReader
  {
    bool owns;
    long startOffset;
    System.IO.Stream stream;

    public StreamReader(System.IO.Stream s, long offset = 0, bool takeOwnership = false)
    {
      stream = s;
      owns = takeOwnership;
      startOffset = offset;
    }
    public void Dispose()
    {
      if(owns)
      {
        stream.Dispose();
      }
    }

    public void Read(long pos, byte[] buf, int offset, int count)
    {
      stream.Position = pos + startOffset;
      stream.Read(buf, offset, count);
    }
  }
}
