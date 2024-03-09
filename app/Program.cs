using System.Text.Json;
using LibGit2Sharp;

internal class Program
{
	private static readonly JsonSerializerOptions _serializerOptions = new() {
		WriteIndented        = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private static async Task Main(string[] args)
	{
		var config            = JsonSerializer.Deserialize<Configuration>(File.ReadAllText("data/options.json"), _serializerOptions)
			?? throw new InvalidOperationException("Unable to read configuration from data/options.json");
		var authorIdentity    = new Identity(config.CommitterName, config.CommitterEmail);
		var committerIdentity = new Identity("ESPHome Manager Addon", config.CommitterEmail);
		var checkDelay        = TimeSpan.FromSeconds(config.CheckPeriodSeconds);
		var credentials       = new UsernamePasswordCredentials() {
			Username = config.RepositoryUsername,
			Password = config.RepositoryPassword,
		};
		var pushOptions       = new PushOptions() {
			CredentialsProvider = (_, _, _) => credentials,
		};

		using var repos = ValidateEnvironment(config, credentials);

		repos.Ignore.AddTemporaryRules([
			"trash/",
		]);

		// ok, we're all set up, one way or another
		// from here on out, our job is to periodically commit any
		// changes and push them to the remote
		while (true) {
			await Task.Delay(checkDelay);

			Console.WriteLine("Looking for changes...");

			var status = repos.RetrieveStatus();

			if (!status.IsDirty) {
				Console.WriteLine("\tNo changes found.");
				continue;
			}

			Commands.Stage(repos, "*");

			status = repos.RetrieveStatus();

			Console.WriteLine("\tItems staged for commit:");

			foreach (var item in status) {
				Console.WriteLine($"\t\t{item.State} {item.FilePath}");
			}

			var commitTime = DateTimeOffset.Now;
			var commit     = repos.Commit("Automatic commit from addon", new Signature(authorIdentity, commitTime), new Signature(committerIdentity, commitTime));

			Console.WriteLine($"\tCommitted {status.Count()} changes as {commit.Id}");

			repos.Network.Push(repos.Head, pushOptions);

			Console.WriteLine("\tPushed changes to remote repository.");
		}
	}

	private static Repository ValidateEnvironment(Configuration config, Credentials credentials)
	{
		bool existingRemoteRefs;
		Repository ret;

		Console.WriteLine("Validating environment:");
		Console.WriteLine($"\tremote repository: {config.RepositoryUri}");
		Console.WriteLine($"\tlocal folder: {config.EsphomePath}");

		// here's what we've got to do to get moving:
		// validate that the URI we were given points to a valid remote; if it doesn't, then fail loudly
		try {
			Console.WriteLine("Checking remote repository...");
			var remoteRefs = Repository.ListRemoteReferences(config.RepositoryUri, (_, _, _) => credentials);
			// if we have remote refs, then our local should already be set up (or empty)

			// otherwise, we can push what we have, if we have anything
			if (remoteRefs.Any()) {
				Console.WriteLine("\tRemote repository exists and is not empty.");
				existingRemoteRefs = true;
			} else {
				Console.WriteLine("\tNo existing remote refs. Remote repository is empty.");
				existingRemoteRefs = false;
			}
		} catch (Exception e) {
			throw new Exception($"The specified remote repository uri [{config.RepositoryUri}] is not valid or not accessible.", e);
		}

		try {
			Console.WriteLine("Checking local repository...");

			// the local path -is- a repository
			ret = new Repository(config.EsphomePath);

			Console.WriteLine("\tLocal repository exists.");
		} catch (RepositoryNotFoundException) {
			var existingLocalFiles = Directory.EnumerateFiles(config.EsphomePath).Any();

			Console.WriteLine("\tLocal repository not yet initialized.");
			Console.WriteLine("Initializing local repository...");

			if (existingLocalFiles && existingRemoteRefs) {
				throw new NotImplementedException("Initializing the local repository while files exist in both the local and remote is not yet implemented.");
				// this case will require some magic:
				// 1) clone the repo somewhere temporary
				// 2) copy the files from the esphome folder into the temp location (overwriting? who knows?)
				// 3) copy the whole thing back to the esphome folder
				// 4) commit and push
				// *** note that this procedure assumes that what's in the local should take precedence over what's in the remote
			} else if (existingLocalFiles) {
				throw new NotImplementedException("Initializing the local repository and pushing to an empty remote is not yet implemented.");
				// we have local files but nothing in the remote; fairly simple procedure:
				// 1) initialize an empty repository in a temp location
				// 2) move the .git folder into the esphome folder
				// 3) add the remote
				// 4) commit and push
			} else {
				// we have a remote repository, either empty or not empty, but no files in the local
				// this case is easy, we'll just clone the remote into the local, even if it's empty
				Console.WriteLine("\tCloning remote repository into local path...");

				CloneIntoExistingFolder(config.RepositoryUri, config.EsphomePath);

				ret = new Repository(config.EsphomePath);
			}
		} catch (Exception e) {
			throw new Exception($"Failed to open local repository at path {config.EsphomePath}", e);
		}

		Console.WriteLine($"Local repository validated.");
		Console.WriteLine($"\tbranch: {ret.Head.CanonicalName}");
		Console.WriteLine($"\tcommit: {ret.Head?.Tip?.Sha}");

		return ret;
	}

	private static void CloneIntoExistingFolder(string remoteUri, string localFolder)
	{
		var tempFolder = Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.User)
			?? Environment.GetEnvironmentVariable("HOME", EnvironmentVariableTarget.User)
			?? throw new InvalidOperationException("Either TEMP or HOME environment variable must be set.");

		if (!Directory.Exists(tempFolder)) {
			throw new InvalidOperationException("Specified (temp|home) directory does not exist.");
		}

		var tfName = Path.Combine(tempFolder, Guid.NewGuid().ToString());

		try {
			var repoFolder = Repository.Clone(remoteUri, tfName);

			// now move tfName/* into localFolder
			foreach (var fse in Directory.EnumerateFiles(tfName)) {
				File.Move(fse, Path.Combine(localFolder, Path.GetFileName(fse)), false);
			}

			foreach (var fse in Directory.EnumerateDirectories(tfName)) {
				//Console.WriteLine($"move {fse} -> {localFolder}");
				Directory.Move(fse, Path.Combine(localFolder, Path.GetFileName(fse)));
			}
		} catch {
			// remove the temp path
			if (Directory.Exists(tfName)) {
				Directory.Delete(tfName, true);
			}
		}
	}
}

internal class Configuration
{
	public string RepositoryUri { get; set; } = "";

	public string EsphomePath { get; set; } = "/homeassistant/esphome";

	public string RepositoryUsername { get; set; } = "";

	public string RepositoryPassword { get; set; } = "";

	public int CheckPeriodSeconds { get; set; } = 300;

	public string CommitterName { get; set; } = "";

	public string CommitterEmail { get; set; } = "";
}
