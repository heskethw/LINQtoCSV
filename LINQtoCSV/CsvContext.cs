﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LINQtoCSV
{
    /// <summary>
    /// Summary description for CsvContext
    /// </summary>
    public static class CsvContext
    {
        #region Read
        
        /// <summary>
        /// Reads the comma separated values from a stream or file.
        /// Returns the data into an IEnumerable that can be used for LINQ queries.
        /// 
        /// The stream or file will be closed after the last line has been processed.
        /// Because the library implements deferred reading (using Yield Return), this may not happen
        /// for a while.
        /// </summary>
        /// <typeparam name="T">The records in the returned IEnumerable will be of this type.</typeparam>
        /// <param name="fileName">The data will be read from this file.</param>
        /// <param name="fileDescription">Additional information how the input file is to be interpreted, such as the culture of the input dates.</param>
        /// <returns>Values read from the stream or file.</returns>
        public static IEnumerable<T> Read<T>(string fileName, CsvFileDescription fileDescription) where T : class, new()
        {
            // Note that ReadData will not be called right away, but when the returned 
            // IEnumerable<T> actually gets accessed.

            var ie = ReadData<T>(fileName, null, fileDescription);
            return ie;
        }

        /// <summary> 
        /// Reads the comma separated values from a stream or file.
        /// Returns the data into an IEnumerable that can be used for LINQ queries.
        /// 
        /// The stream or file will be closed after the last line has been processed.
        /// Because the library implements deferred reading (using Yield Return), this may not happen
        /// for a while.
        /// </summary>
        /// <typeparam name="T">The records in the returned IEnumerable will be of this type.</typeparam>
        /// <param name="stream">The data will be read from this stream.</param>
        /// <returns>Values read from the stream or file.</returns>
        public static IEnumerable<T> Read<T>(StreamReader stream) where T : class, new()
        {
            return Read<T>(stream, new CsvFileDescription());
        }

        /// <summary>
        /// Reads the comma separated values from a stream or file.
        /// Returns the data into an IEnumerable that can be used for LINQ queries.
        /// 
        /// The stream or file will be closed after the last line has been processed.
        /// Because the library implements deferred reading (using Yield Return), this may not happen
        /// for a while.
        /// </summary>
        /// <typeparam name="T">The records in the returned IEnumerable will be of this type.</typeparam>
        /// <param name="fileName">The data will be read from this file.</param>
        /// <returns>Values read from the stream or file.</returns>
        public static IEnumerable<T> Read<T>(string fileName) where T : class, new()
        {
            return Read<T>(fileName, new CsvFileDescription());
        }

        /// <summary>
        /// Reads the comma separated values from a stream or file.
        /// Returns the data into an IEnumerable that can be used for LINQ queries.
        /// 
        /// The stream or file will be closed after the last line has been processed.
        /// Because the library implements deferred reading (using Yield Return), this may not happen
        /// for a while.
        /// </summary>
        /// <typeparam name="T">The records in the returned IEnumerable will be of this type.</typeparam>
        /// <param name="stream">The data will be read from this stream.</param>
        /// <param name="fileDescription">Additional information how the input file is to be interpreted, such as the culture of the input dates.</param>
        /// <returns>Values read from the stream or file.</returns>
        public static IEnumerable<T> Read<T>(StreamReader stream, CsvFileDescription fileDescription) where T : class, new()
        {
            return ReadData<T>(null, stream, fileDescription);
        }

        /// <summary>
        /// Read Data
        /// </summary>
        /// <typeparam name="T">The records in the returned IEnumerable will be of this type.</typeparam>
        /// <param name="fileName">
        /// Name of the file associated with the stream.
        /// 
        /// If this is not null, a file is opened with this name.
        /// If this is null, the method attempts to read from the passed in stream.
        /// </param>
        /// <param name="stream">
        /// All data is read from this stream, unless fileName is not null.
        /// 
        /// This is a StreamReader rather then a TextReader,
        /// because we need to be able to seek back to the start of the
        /// stream, and you can't do that with a TextReader (or StringReader).
        /// </param>
        /// <param name="fileDescription">Additional information how the input file is to be interpreted, such as the culture of the input dates.</param>
        /// <returns>Values read from the stream or file.</returns>
        private static IEnumerable<T> ReadData<T>(string fileName, StreamReader stream, CsvFileDescription fileDescription) where T : class, new()
        {
            // If T implements IDataRow, then we're reading raw data rows 
            var readingRawDataRows = typeof(IDataRow).GetTypeInfo().IsAssignableFrom(typeof(T));

            // The constructor for FieldMapper_Reading will throw an exception if there is something
            // wrong with type T. So invoke that constructor before you open the file, because if there
            // is an exception, the file will not be closed.
            //
            // If T implements IDataRow, there is no need for a FieldMapper, because in that case we're returning
            // raw data rows.
            FieldMapper_Reading<T> fm = null;

            if (!readingRawDataRows)
            {
                fm = new FieldMapper_Reading<T>(fileDescription, fileName, false);
            }

            // -------
            // Each time the IEnumerable<T> that is returned from this method is 
            // accessed in a foreach, ReadData is called again (not the original Read overload!)
            //
            // So, open the file here, or rewind the stream.

            var readingFile = !string.IsNullOrEmpty(fileName);

            if (readingFile)
            {
                stream = new StreamReader(
                    File.Open(fileName, FileMode.Open),
                    fileDescription.TextEncoding,
                    fileDescription.DetectEncodingFromByteOrderMarks);
            }
            else
            {
                // Rewind the stream

                if ((stream == null) || (!stream.BaseStream.CanSeek))
                {
                    throw new BadStreamException();
                }

                stream.BaseStream.Seek(0, SeekOrigin.Begin);
            }

            // ----------

            var cs = new CsvStream(stream, null, fileDescription.SeparatorChar, fileDescription.IgnoreTrailingSeparatorChar);

            // If we're reading raw data rows, instantiate a T so we return objects
            // of the type specified by the caller.
            // Otherwise, instantiate a DataRow, which also implements IDataRow.
            IDataRow row;
            if (readingRawDataRows)
            {
                row = new T() as IDataRow;
            }
            else
            {
                row = new DataRow();
            }

            var ae = new AggregatedException(typeof(T).ToString(), fileName, fileDescription.MaximumNbrExceptions);

            try
            {
                List<int> charLengths = null;
                if (!readingRawDataRows)
                {
                    charLengths = fm.GetCharLengths();
                }

                var firstRow = true;
                while (cs.ReadRow(row, charLengths))
                {
                    // Skip empty lines.
                    // Important. If there is a newline at the end of the last data line, the code
                    // thinks there is an empty line after that last data line.
                    if ((row.Count == 1) &&
                        ((row[0].Value == null) ||
                         (string.IsNullOrEmpty(row[0].Value.Trim()))))
                    {
                        continue;
                    }

                    if (firstRow && fileDescription.FirstLineHasColumnNames)
                    {
                        if (!readingRawDataRows) { fm.ReadNames(row); }
                    }
                    else
                    {
                        var obj = default(T);
                        try
                        {
                            if (readingRawDataRows)
                            {
                                obj = row as T;
                            }
                            else
                            {
                                obj = fm.ReadObject(row, ae);
                            }
                        }
                        catch (AggregatedException)
                        {
                            // Seeing that the AggregatedException was thrown, maximum number of exceptions
                            // must have been reached, so rethrow.
                            // Catch here, so you don't add an AggregatedException to an AggregatedException
                            throw;
                        }
                        catch (Exception e)
                        {
                            // Store the exception in the AggregatedException ae.
                            // That way, if a file has many errors leading to exceptions,
                            // you get them all in one go, packaged in a single aggregated exception.
                            ae.AddException(e);
                        }

                        yield return obj;
                    }
                    firstRow = false;
                }
            }
            finally
            {
                if (readingFile)
                {
                    stream.Dispose();
                }

                // If any exceptions were raised while reading the data from the file,
                // they will have been stored in the AggregatedException ae.
                // In that case, time to throw ae.
                ae.ThrowIfExceptionsStored();
            }
        }
        
        #endregion
        #region Write
        
        public static void Write<T>(IEnumerable<T> values, string fileName, CsvFileDescription fileDescription)
        {
            using (var sw = new StreamWriter(File.Open(fileName, FileMode.OpenOrCreate), fileDescription.TextEncoding))
            {
                WriteData(values, fileName, sw, fileDescription);
            }
        }

        public static void Write<T>(IEnumerable<T> values, TextWriter stream)
        {
            Write(values, stream, new CsvFileDescription());
        }

        public static void Write<T>(IEnumerable<T> values, string fileName)
        {
            Write(values, fileName, new CsvFileDescription());
        }

        public static void Write<T>(IEnumerable<T> values, TextWriter stream, CsvFileDescription fileDescription)
        {
            WriteData(values, null, stream, fileDescription);
        }
        
        private static void WriteData<T>(IEnumerable<T> values, string fileName, TextWriter stream, CsvFileDescription fileDescription)
        {
            var fm = new FieldMapper<T>(fileDescription, fileName, true);
            var cs = new CsvStream(null, stream, fileDescription.SeparatorChar, fileDescription.IgnoreTrailingSeparatorChar);

            var row = new List<string>();

            // If first line has to carry the field names, write the field names now.
            if (fileDescription.FirstLineHasColumnNames)
            {
                fm.WriteNames(row);
                cs.WriteRow(row, fileDescription.QuoteAllFields);
            }

            // -----

            foreach (var obj in values)
            {
                // Convert obj to row
                fm.WriteObject(obj, row);
                cs.WriteRow(row, fileDescription.QuoteAllFields);
            }
        }

        #endregion
    }
}
