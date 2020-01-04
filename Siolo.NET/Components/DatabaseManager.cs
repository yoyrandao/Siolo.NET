﻿using System.Threading.Tasks;
using Siolo.NET.Components.Neo4j;
using Siolo.NET.Components.Postgre;

namespace Siolo.NET.Components
{
	public class DatabaseManager
	{
		private const string Host = "104.248.28.149";

		public Mongo Mongo { get; }

		public Redis Redis { get; }

		public Neo4J Neo4J { get; }

		public Postgres Postgres { get; }

		public Elastic Elastic { get; }

		public VT.VirusTotal VirusTotal { get; }

		public DatabaseManager()
		{
			Mongo = new Mongo(Host, "27017", "vt_reports", "short_vt_reports");
			Redis = new Redis(Host, "6379");
			Neo4J = new Neo4J(Host, "7687", "neo4j", "test");
			Postgres = new Postgres(Host, "5432");
			Elastic = new Elastic();
			VirusTotal = new VT.VirusTotal(@"Resources\.virustotal.api", @"Resources\sigs.json", Mongo);
		}

		public async Task UpdateRedisStorage()
		{
			await Redis.PushHostData(await Postgres.GetEntities());
		}

		public async Task<bool> PushNewHost(string ip)
		{
			string subnetIp = await Neo4J.PushHostRaw(ip);

			if (subnetIp != default)
			{
				var parentSubnetPolicies = Redis.GetHostWildcarts(subnetIp);

				await foreach (var policy in parentSubnetPolicies)
				{
					await Postgres.AttachPolicy(ip, policy);
				}
			}

			if (!await Postgres.InsertNewOne(ip))
			{
				return false;
			}

			await UpdateRedisStorage();

			return true;
		}
	}
}
