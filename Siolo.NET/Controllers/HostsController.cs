﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Newtonsoft.Json;

using Siolo.NET.Components;
using Siolo.NET.Components.Logstash;
using Siolo.NET.Components.Neo4j;
using Siolo.NET.Components.Network;
using Siolo.NET.Models;

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
// ReSharper disable All

namespace Siolo.NET.Controllers
{
	[ApiController]
	public class HostsController : ControllerBase
	{
		private readonly DatabaseManager _manager;

		private readonly Response _response;

		public HostsController(DatabaseManager manager)
		{
			_manager = manager;
			_response = new Response();
		}

		[Route("api/wildcart"), HttpPost]
		public async Task<IActionResult> AddWildcart([FromBody] PolicyContract contract)
		{
			try
			{
				if (!Regex.IsMatch(contract.Wildcart, @"^\w+|\*:\w+|\*$"))
				{
					return Ok(_response.SetStatus(true, "OK. Invalid wildcard format"));
				}

				bool registerResult = await _manager.Postgres.RegisterPolicy(contract.Info, contract.Wildcart);

				return Ok(_response.SetStatus(true, $"OK. {(registerResult ? "Successfully registered" : "Already exists")}"));
			}
			catch (Exception e)
			{
				return BadRequest(_response.SetStatus(false, $"NOK. {e.Message}"));
			}
		}

		[Route("api/attach"), HttpPost]
		public async Task<IActionResult> Attach([FromBody] AttachContract contract)
		{
			try
			{
				if (!Regex.IsMatch(contract.Wildcart, @"^\w+|\*:\w+|\*$"))
				{
					return Ok(_response.SetStatus(true, "OK. Invalid wildcard format"));
				}

				if (!Regex.IsMatch(contract.Ip, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\/\d{1,2}$"))
				{
					return Ok(_response.SetStatus(true, "OK. Invalid ip format"));
				}

				await _manager.Postgres.AttachPolicy(contract.Ip, contract.Wildcart);
				await _manager.Redis.PushHostData(await _manager.Postgres.GetEntities());

				return Ok(_response.SetStatus(true, "OK"));
			}
			catch (Exception e)
			{
				return BadRequest(_response.SetStatus(false, $"NOK. {e.Message}"));
			}
		}

		[Route("api/link"), HttpPost]
		public async Task<IActionResult> LinkSubnets([FromBody] RelationContract contract)
		{
			try
			{
				if (!Regex.IsMatch(contract.First, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\/\d{1,2}$"))
				{
					return Ok(_response.SetStatus(true, $"OK. Invalid ip format in \"{contract.First}\""));
				}

				if (!Regex.IsMatch(contract.Second, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\/\d{1,2}$"))
				{
					return Ok(_response.SetStatus(true, $"OK. Invalid ip format in \"{contract.Second}\""));
				}

				await _manager.Neo4J.CreateRelation(new NodeEntity(contract.First, true),
														new NodeEntity(contract.Second, true));

				return Ok(_response.SetStatus(true, "OK"));
			}
			catch (Exception e)
			{
				return BadRequest(_response.SetStatus(false, $"NOK. {e.Message}"));
			}
		}

		[Route("api/upload"), HttpPost]
		public async Task<IActionResult> UploadFile([FromForm] IFormFile file, [FromForm] string host)
		{
			try
			{
				using (var memoryStream = new MemoryStream())
				{
					file.OpenReadStream().CopyTo(memoryStream);

					var policies = _manager.Redis.GetHostWildcards(host);
					var shortReport = await _manager.VirusTotal.GetShortReportFromFileBytesAsync(memoryStream.ToArray());

					await _manager.Logstash.SendEventAsync(new EventDrop(host, shortReport.md5, shortReport.full_class));

					var restrictingPolicy = await NetworkUtility.GetRestrictingPolicy(policies, shortReport.full_class.ToLower());

					if (file != null && restrictingPolicy != "")
					{
						await _manager.RegisterIncident(file, host, shortReport, restrictingPolicy);
						return Ok(_response.SetStatus(true, "OK. Incident registered"));
					}

					return Ok(_response.SetStatus(true, "OK."));
				}
			}
			catch (Exception e)
			{
				return BadRequest(_response.SetStatus(false, $"NOK. {e.Message}, {e.StackTrace}"));
			}
		}

		[Route("api/find_paths"), HttpPost]
		public async Task<IActionResult> FindPaths([FromBody] RelationContract relation)
		{
			try
			{
				return Ok(await _manager.Neo4J.FindAllPaths(relation.First, relation.Second));
			}
			catch (Exception e)
			{
				return BadRequest(_response.SetStatus(false, $"NOK. {e.Message}"));
			}
		}

		[Route("api/get_active_hosts"), HttpPost]
		public async Task<IActionResult> GetActiveHosts()
		{
			try
			{
				return Ok(_response.SetStatus(true, JsonConvert.SerializeObject(await _manager.Neo4J.GetAllHosts())));
			}
			catch (Exception e)
			{
				return BadRequest(_response.SetStatus(false, $"NOK. {e.Message}"));
			}
		}

		[Route("api/find_incs"), HttpPost]
		public async Task<IActionResult> FindAllIncidents()
		{
			try
			{
				return Ok(_response.SetStatus(true, JsonConvert.SerializeObject(await _manager.Elastic.FindAllIncidents())));
			}
			catch (Exception e)
			{
				return BadRequest(_response.SetStatus(false, $"NOK. {e.Message}"));
			}
		}

		[Route("api/get_short_report"), HttpPost]
		public async Task<IActionResult> GetShortReport([FromForm] string hash)
		{
			try
			{
				return Ok(_response.SetStatus(true, await _manager.Mongo.GetReport(hash, true)));
			}
			catch (Exception e)
			{
				return BadRequest(_response.SetStatus(false, $"NOK. {e.Message}"));
			}
		}

		[Route("api/find_inc"), HttpPost]
		public async Task<IActionResult> FindIncident([FromForm] string id)
		{
			try
			{
				var hits = await _manager.Elastic.FindIncident(id);

				return Ok(_response.SetStatus(true, JsonConvert.SerializeObject(hits)));
			}
			catch (Exception e)
			{
				return BadRequest(_response.SetStatus(false, $"NOK. {e.Message}"));
			}
		}
	}
}