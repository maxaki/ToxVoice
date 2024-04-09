using System.Net;
using System.Net.Http.Headers;

namespace ToxVoice.TranscriptionProcessing;

public class DiscordHttpClient : IDisposable
{
	private bool _disposed;
	private readonly bool _hideSteamId;
	private readonly HttpClient _httpClient;

	public DiscordHttpClient(Uri webhookUri, bool hideSteamId)
	{
		_hideSteamId = hideSteamId;
		_httpClient = new HttpClient();
		_httpClient.BaseAddress = webhookUri;
	}

	private MultipartFormDataContent CreateMultipartContent(string steamId, Transcription.Transcription transcription, int violatedFilterWeight)
	{
		var content = new MultipartFormDataContent();
		var fileContent = new ByteArrayContent(transcription.Data, 0, transcription.DataLength);
		fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/mp3");
		content.Add(fileContent, "file", $"{transcription.ToxVoiceUserId}.mp3");

		var userId = transcription.ToxVoiceUserId;
		if (!_hideSteamId)
		{
			if (!string.IsNullOrEmpty(steamId))
			{
				userId = steamId;
			}
		}

		var messageContent = $"{userId}```{transcription.Text}```";
		if (violatedFilterWeight > 0)
		{
			messageContent += $"Weight: {violatedFilterWeight}";
		}

		var textContent = new StringContent(messageContent);
		content.Add(textContent, "content");

		return content;
	}

	public async Task<HttpResponseMessage> SendMessageWithRetryAsync(string steamId, Transcription.Transcription transcription, int violatedFilterWeight, CancellationToken cancellationToken)
	{
		const int maxRetries = 3;
		const int retryDelay = 1000;
		using var content = CreateMultipartContent(steamId, transcription, violatedFilterWeight);
		for (var retry = 0; retry < maxRetries; retry++)
		{
			try
			{
				var response = await _httpClient.PostAsync("", content, cancellationToken).ConfigureAwait(false);
				if (response.IsSuccessStatusCode)
					return response;

				if (IsRateLimited(response))
				{
					var resetAfter = GetRateLimitResetAfter(response);
					Console.WriteLine($"[ToxVoice] Discord Rate limited. Retrying after {resetAfter.TotalSeconds} seconds.");
					await Task.Delay(resetAfter, cancellationToken).ConfigureAwait(false);
					continue;
				}
			}
			catch
			{
			}

			Console.WriteLine($"[ToxVoice] Failed to upload discord file. Retry attempt: {retry + 1}");
			await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
		}

		return new HttpResponseMessage(HttpStatusCode.InternalServerError);
	}

	private static TimeSpan GetRateLimitResetAfter(HttpResponseMessage response)
	{
		if (!response.Headers.TryGetValues("X-RateLimit-Reset-After", out var values))
			return TimeSpan.FromSeconds(60);

		return double.TryParse(values.FirstOrDefault(), out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(60);
	}

	private static bool IsRateLimited(HttpResponseMessage response) => response.StatusCode == HttpStatusCode.TooManyRequests ||
	                                                                   response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && values.FirstOrDefault() == "0";

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_httpClient.Dispose();
	}
}