using System.Text.RegularExpressions;
using ToxVoice.Logging;
using ToxVoice.ToxVoiceConfiguration;

namespace ToxVoice.TranscriptionProcessing;

public class TranscriptionFilter
{
	public readonly List<TriggerFilter> Filters;

	public TranscriptionFilter(ConfigurationFile configurationFile)
	{
		Filters = configurationFile.TriggerFilter.Filters;
	}

	public int GetViolatedFilterWeight(string text) => GetViolatedFilterWeightCore(text, Filters);

	private static int GetViolatedFilterWeightCore(string text, List<TriggerFilter> filters)
	{
		var totalWeight = 0;
		foreach (var filter in filters)
		{
			if (filter.Regex)
			{
				if (!IsRegexMatch(text, filter.Triggers))
					continue;
			}
			else
			{
				if (!IsWordListContained(text, filter))
					continue;
			}

			totalWeight += filter.Weight;
		}

		return totalWeight;
	}

	private static bool IsRegexMatch(string text, List<string> regexPatterns)
	{
		foreach (var pattern in regexPatterns)
		{
			try
			{
				if (!Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
				{
					return false;
				}
			}
			catch(Exception exception)
			{
				Log.Error("Error in regex pattern: " + pattern);
			}
		}

		return true;
	}

	private static bool IsWordListContained(ReadOnlySpan<char> text, TriggerFilter triggerFilter)
	{
		foreach (var word in triggerFilter.Triggers)
		{
			if (!ContainsWord(text, word.AsSpan()))
			{
				return false;
			}
		}

		return true;
	}

	private static bool ContainsWord(ReadOnlySpan<char> text, ReadOnlySpan<char> word)
	{
		var textLength = text.Length;
		var wordLength = word.Length;

		for (var i = 0; i <= textLength - wordLength; i++)
		{
			var substring = text.Slice(i, wordLength);
			if (substring.Length == wordLength && substring.Equals(word, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}