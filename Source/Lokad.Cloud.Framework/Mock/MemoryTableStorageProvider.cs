﻿#region Copyright (c) Lokad 2009-2010
// This code is released under the terms of the new BSD licence.
// URL: http://www.lokad.com/
#endregion

using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Linq;
using Lokad.Cloud.Storage;
using Lokad.Cloud.Storage.Azure;

namespace Lokad.Cloud.Mock
{
	/// <summary>Mock in-memory TableStorage Provider.</summary>
	/// <remarks>
	/// All the methods of <see cref="MemoryTableStorageProvider"/> are thread-safe.
	/// </remarks>
	public class MemoryTableStorageProvider : ITableStorageProvider
	{
		/// <summary>In memory table storage : entries per table (designed for simplicity instead of performance)</summary>
		readonly Dictionary<string, List<MockTableEntry>> _tables;

		/// <summary>Formatter as requiered to handle FatEntities.</summary>
		readonly IBinaryFormatter _formatter;

		/// <summary>naive global lock to make methods thread-safe.</summary>
		readonly object _syncRoot;

		int _nextETag;

		/// <summary>
		/// Constructor for <see cref="MemoryTableStorageProvider"/>.
		/// </summary>
		public MemoryTableStorageProvider()
		{
			_tables = new Dictionary<string, List<MockTableEntry>>();
			_syncRoot = new object();
			_formatter = new CloudFormatter();
		}

		/// <see cref="ITableStorageProvider.CreateTable"/>
		public bool CreateTable(string tableName)
		{
			lock (_syncRoot)
			{
				if (_tables.ContainsKey(tableName))
				{
					//If the table already exists: return false.
					return false;
				}

				//create table return true.
				_tables.Add(tableName, new List<MockTableEntry>());
				return true;
			}
		}

		/// <see cref="ITableStorageProvider.DeleteTable"/>
		public bool DeleteTable(string tableName)
		{
			lock (_syncRoot)
			{
				if (_tables.ContainsKey(tableName))
				{
					//If the table exists remove it.
					_tables.Remove(tableName);
					return true;
				}
				
				//Can not remove an unexisting table.
				return false;
			}
		}

		/// <see cref="ITableStorageProvider.GetTables"/>
		public IEnumerable<string> GetTables()
		{
			lock (_syncRoot)
			{
				return _tables.Keys;
			}
		}

		/// <see cref="ITableStorageProvider.Get{T}(string)"/>
		IEnumerable<CloudEntity<T>> GetInternal<T>(string tableName, Func<MockTableEntry,bool> predicate)
		{
			lock (_syncRoot)
			{
				if (!_tables.ContainsKey(tableName))
				{
					return new List<CloudEntity<T>>();
				}

				return from entry in _tables[tableName]
					   where predicate(entry)
					   select entry.ToCloudEntity<T>(_formatter);
			}
		}

		/// <see cref="ITableStorageProvider.Get{T}(string)"/>
		public IEnumerable<CloudEntity<T>> Get<T>(string tableName)
		{
			return GetInternal<T>(tableName, entry => true);
		}

		/// <see cref="ITableStorageProvider.Get{T}(string,string)"/>
		public IEnumerable<CloudEntity<T>> Get<T>(string tableName, string partitionKey)
		{
			return GetInternal<T>(tableName, entry => entry.PartitionKey == partitionKey);
		}

		/// <see cref="ITableStorageProvider.Get{T}(string,string,string,string)"/>
		public IEnumerable<CloudEntity<T>> Get<T>(string tableName, string partitionKey, string startRowKey, string endRowKey)
		{
			var isInRange = string.IsNullOrEmpty(endRowKey)
				? (Func<string, bool>)(rowKey => string.Compare(startRowKey, rowKey) <= 0)
				: (rowKey => string.Compare(startRowKey, rowKey) <= 0 && string.Compare(rowKey, endRowKey) < 0);

			return GetInternal<T>(tableName, entry => entry.PartitionKey == partitionKey && isInRange(entry.RowKey))
				.OrderBy(entity => entity.RowKey);
		}

		/// <see cref="ITableStorageProvider.Get{T}(string,string,System.Collections.Generic.IEnumerable{string})"/>
		public IEnumerable<CloudEntity<T>> Get<T>(string tableName, string partitionKey, IEnumerable<string> rowKeys)
		{
			var keys = rowKeys.ToSet();
			return GetInternal<T>(tableName, entry => entry.PartitionKey == partitionKey && keys.Contains(entry.RowKey));
		}

		/// <see cref="ITableStorageProvider.Insert{T}"/>
		public void Insert<T>(string tableName, IEnumerable<CloudEntity<T>> entities)
		{
			lock (_syncRoot)
			{
				List<MockTableEntry> entries;
				if (!_tables.TryGetValue(tableName, out entries))
				{
					_tables.Add(tableName, entries = new List<MockTableEntry>());
				}

				// verify valid data BEFORE inserting them
				if (entities.Join(entries, u => ToId(u), ToId, (u, v) => true).Any())
				{
					throw new DataServiceRequestException("INSERT: key conflict.");
				}
				if (entities.GroupBy(e => ToId(e)).Any(id => id.Count() != 1))
				{
					throw new DataServiceRequestException("INSERT: duplicate keys.");
				}

				// ok, we can insert safely now
				foreach (var entity in entities)
				{
					var etag = (_nextETag++).ToString();
					entity.ETag = etag;
					entries.Add(new MockTableEntry
						{
							PartitionKey = entity.PartitionKey,
							RowKey = entity.RowKey,
							ETag = etag,
							Value = FatEntity.Convert(entity, _formatter)
						});
				}
			}
		}

		/// <see cref="ITableStorageProvider.Update{T}"/>
		public void Update<T>(string tableName, IEnumerable<CloudEntity<T>> entities, bool force)
		{
			lock (_syncRoot)
			{
				List<MockTableEntry> entries;
				if (!_tables.TryGetValue(tableName, out entries))
				{
					throw new DataServiceRequestException("UPDATE: table not found.");
				}

				// verify valid data BEFORE updating them
				if (entities.GroupJoin(entries, u => ToId(u), ToId, (u, vs) => vs.Count(entry => force || entry.ETag == u.ETag)).Any(c => c != 1))
				{
					throw new DataServiceRequestException("UPDATE: key not found or etag conflict.");
				}
				if (entities.GroupBy(e => ToId(e)).Any(id => id.Count() != 1))
				{
					throw new DataServiceRequestException("UPDATE: duplicate keys.");
				}

				// ok, we can update safely now
				foreach (var entity in entities)
				{
					var etag = (_nextETag++).ToString();
					entity.ETag = etag;
					var index = entries.FindIndex(entry => entry.PartitionKey == entity.PartitionKey && entry.RowKey == entity.RowKey);
					entries[index] = new MockTableEntry
						{
							PartitionKey = entity.PartitionKey,
							RowKey = entity.RowKey,
							ETag = etag,
							Value = FatEntity.Convert(entity, _formatter)
						};
				}
			}
		}
		/// <see cref="ITableStorageProvider.Update{T}"/>
		public void Upsert<T>(string tableName, IEnumerable<CloudEntity<T>> entities)
		{
			lock (_syncRoot)
			{
				// deleting all existing entities
				entities.GroupBy(e => e.PartitionKey)
					.ForEach(g => Delete<T>(tableName, g.Key, g.Select(e => e.RowKey)));

				// inserting all entities
				Insert(tableName, entities);
			}
		}

		/// <see cref="ITableStorageProvider.Delete{T}"/>
		public void Delete<T>(string tableName, string partitionKey, IEnumerable<string> rowKeys)
		{
			lock (_syncRoot)
			{
				List<MockTableEntry> entries;
				if (!_tables.TryGetValue(tableName, out entries))
				{
					return;
				}

				var keys = rowKeys.ToSet();
				entries.RemoveAll(entry => entry.PartitionKey == partitionKey && keys.Contains(entry.RowKey));
			}
		}

		/// <see cref="ITableStorageProvider.Delete{T}"/>
		public void Delete<T>(string tableName, IEnumerable<CloudEntity<T>> entities, bool force)
		{
			lock (_syncRoot)
			{
				List<MockTableEntry> entries;
				if (!_tables.TryGetValue(tableName, out entries))
				{
					return;
				}

				var entityList = entities.ToList();

				// verify valid data BEFORE updating them
				if (entityList.Join(entries, u => ToId(u), ToId, (u, v) => force || u.ETag == v.ETag).Any(c => !c))
				{
					throw new DataServiceRequestException("UPDATE: etag conflict.");
				}

				// ok, we can update safely now
				entries.RemoveAll(entry => entityList.Exists(entity => entity.PartitionKey == entry.PartitionKey && entity.RowKey == entry.RowKey));
			}
		}

		static Tuple<string, string> ToId<T>(CloudEntity<T> entity)
		{
			return Tuple.From(entity.PartitionKey, entity.RowKey);
		}
		static Tuple<string, string> ToId(MockTableEntry entry)
		{
			return Tuple.From(entry.PartitionKey, entry.RowKey);
		}

		class MockTableEntry
		{
			public string PartitionKey { get; set; }
			public string RowKey { get; set; }
			public string ETag { get; set; }
			public FatEntity Value { get; set; }

			public CloudEntity<T> ToCloudEntity<T>(IBinaryFormatter formatter)
			{
				return FatEntity.Convert<T>(Value, formatter, ETag);
			}
		}
	}
}
