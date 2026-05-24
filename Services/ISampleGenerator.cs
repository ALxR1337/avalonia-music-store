using System.Threading;
using System.Threading.Tasks;

namespace MusicApp.Services;

public interface ISampleGenerator
{
    Task GenerateAsync(string sourcePath, int startSeconds, int durationSeconds, string outputPath, CancellationToken cancel = default);
}
