using SkiaSharp;

namespace Chipmunk.Platform;

public static class ImageValidation
{
    public static (int Width, int Height) Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) throw new ApiError(400, "图片无法解码", "Image could not be decoded");
        var info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || info.Width > 4096 || info.Height > 4096 || (long)info.Width * info.Height > 12_000_000 || codec.FrameCount > 1)
            throw new ApiError(400, "图片需为4096像素以内、最多1200万像素的静态图片", "Use a static image with dimensions up to 4096 and at most 12 megapixels");
        using var bitmap = new SKBitmap(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new ApiError(400, "图片数据损坏或不完整", "Image data is corrupt or incomplete");
        return (info.Width, info.Height);
    }
}
