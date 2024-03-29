﻿using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Siolo.NET.Components
{
	public class Mongo
	{
		private readonly MongoClient _client;

		private readonly IMongoDatabase _db;

		private readonly IMongoCollection<BsonDocument> _reportCollection;
		private readonly IMongoCollection<BsonDocument> _shortReportCollection;

		private readonly IGridFSBucket _gridfs;

		public Mongo(string host, string port, string login, string password, string reportCollection, string shortReportCollection)
		{
			string connection = $"mongodb://{login}:{password}@{host}:{port}";

			_client = new MongoClient(connection);

			_db = _client.GetDatabase("storage");

			_reportCollection = _db.GetCollection<BsonDocument>(reportCollection);
			_shortReportCollection = _db.GetCollection<BsonDocument>(shortReportCollection);

			_gridfs = new GridFSBucket(_db);
		}

		private async Task<bool> HasDocumentByHash(string hash, bool isShort = false)
		{
			string data = await GetReport(hash, isShort);

			return data != string.Empty;
		}

		public async Task InsertReport(string hash, string data)
		{
			if (!await HasDocumentByHash(hash))
			{
				var bson = new Dictionary<string, string>() {
					{ "hash" , hash },
					{ "data" , data }
				}.ToBsonDocument();

				await _reportCollection.InsertOneAsync(bson);
			}
		}

		public async Task InsertShortReport(string hash, string data)
		{
			if (!await HasDocumentByHash(hash, true))
			{
				var bson = new Dictionary<string, string>()
				{
					{ "hash", hash },
					{ "data", data }
				}.ToBsonDocument();

				await _shortReportCollection.InsertOneAsync(bson);
			}
		}

		public async Task<string> GetReport(string hash, bool isShort = false)
		{
			var result = !isShort
						 ? await _reportCollection.FindAsync(new BsonDocument("hash", hash))
						 : await _shortReportCollection.FindAsync(new BsonDocument("hash", hash));

			var singleOrDefault = result.SingleOrDefault();
			var data = singleOrDefault?["data"];

			return data is null ? "" : data.ToString();
		}

		public async Task<string> UploadFile(string filePath)
		{
			await using (var fs = new FileStream(filePath, FileMode.Open))
			{
				var id = await _gridfs.UploadFromStreamAsync(Path.GetFileName(filePath), fs);

				return id.ToString();
			}
		}

		public async Task<string> UploadFile(string fileName, Stream fileStream)
		{
			var id = await _gridfs.UploadFromStreamAsync(fileName, fileStream);

			return id.ToString();
		}

		public async Task GetFile(string filename)
		{
			await using (var fs = new FileStream(filename, FileMode.OpenOrCreate))
			{
				await _gridfs.DownloadToStreamByNameAsync(filename, fs);
			}
		}
	}
}
