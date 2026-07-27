using Img2PDF.Core.Pdf;

string? listPath = null;
string? outputPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--list" when i + 1 < args.Length:
            listPath = args[++i];
            break;
        case "--output" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
    }
}

if (listPath is null || outputPath is null)
{
    Console.Error.WriteLine("Usage: Img2PDF.Cli --list <path-to-list-file> --output <output.pdf>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("The list file is UTF-8, one absolute image path per line (same shape the");
    Console.Error.WriteLine("shell extension's temp-file handshake will use in a later milestone).");
    return 1;
}

if (!File.Exists(listPath))
{
    Console.Error.WriteLine($"List file not found: {listPath}");
    return 1;
}

string[] imagePaths = File.ReadAllLines(listPath, System.Text.Encoding.UTF8)
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .ToArray();

if (imagePaths.Length == 0)
{
    Console.Error.WriteLine("List file contained no image paths.");
    return 1;
}

Console.WriteLine($"Composing {imagePaths.Length} page(s) -> {outputPath}");

await PdfComposer.ComposeAsync(imagePaths, outputPath);

long inputBytes = imagePaths.Sum(p => new FileInfo(p).Length);
long outputBytes = new FileInfo(outputPath).Length;
double deltaPct = (double)(outputBytes - inputBytes) / inputBytes;

Console.WriteLine($"Input total:  {inputBytes,12:N0} bytes");
Console.WriteLine($"Output PDF:   {outputBytes,12:N0} bytes");
Console.WriteLine($"Delta:        {deltaPct:P1} {(Math.Abs(deltaPct) <= 0.10 ? "(within 10% target)" : "(EXCEEDS 10% target - check JPEG passthrough)")}");

return 0;
