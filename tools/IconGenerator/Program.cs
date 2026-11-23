using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGenerator;

class Program
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("Bắt đầu tạo icon WorkManagement...");

        string outputPath = Path.GetFullPath(@"src\WorkManagement\Assets\app.ico");
        string pngOutputPath = Path.GetFullPath(@"src\WorkManagement\Assets\app.png");
        string dir = Path.GetDirectoryName(outputPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // Tạo hình ảnh gốc ở độ phân giải cao 512x512
        int masterSize = 512;
        var masterBitmap = RenderMasterIcon(masterSize);

        // Lưu bản PNG chất lượng cao
        using (var fs = new FileStream(pngOutputPath, FileMode.Create))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(masterBitmap));
            encoder.Save(fs);
        }
        Console.WriteLine($"Đã tạo file PNG: {pngOutputPath}");

        // Các kích thước chuẩn Windows Icon
        int[] iconSizes = new[] { 256, 128, 64, 48, 32, 24, 16 };
        var pngDataList = new List<(int size, byte[] data)>();

        foreach (int size in iconSizes)
        {
            var resized = ResizeBitmap(masterBitmap, size, size);
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(resized));
            encoder.Save(ms);
            pngDataList.Add((size, ms.ToArray()));
        }

        // Đóng gói thành file .ico chuẩn Windows
        using (var fs = new FileStream(outputPath, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            // ICONHEADER (6 bytes)
            bw.Write((ushort)0); // Reserved
            bw.Write((ushort)1); // Type 1 = Icon
            bw.Write((ushort)pngDataList.Count); // Count

            // Tính toán offset của các ảnh (Header = 6 + Count * 16)
            int offset = 6 + (pngDataList.Count * 16);

            // Ghi ICONDIRENTRY cho từng kích thước
            foreach (var item in pngDataList)
            {
                byte width = item.size >= 256 ? (byte)0 : (byte)item.size;
                byte height = item.size >= 256 ? (byte)0 : (byte)item.size;

                bw.Write(width);              // Width
                bw.Write(height);             // Height
                bw.Write((byte)0);            // Color count
                bw.Write((byte)0);            // Reserved
                bw.Write((ushort)1);          // Color planes
                bw.Write((ushort)32);         // Bits per pixel
                bw.Write((uint)item.data.Length); // Image size in bytes
                bw.Write((uint)offset);       // Image offset
                offset += item.data.Length;
            }

            // Ghi dữ liệu ảnh PNG
            foreach (var item in pngDataList)
            {
                bw.Write(item.data);
            }
        }

        Console.WriteLine($"Đã tạo file ICO thành công tại: {outputPath}");
    }

    static RenderTargetBitmap RenderMasterIcon(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = size;

            // 1. NỀN CHÍNH: Bo góc Squircle với Gradient sang trọng tông Vàng/Cam
            var bgBrush = new LinearGradientBrush(
                Color.FromRgb(0x1A, 0x11, 0x0A), // Dark Warm Amber Charcoal
                Color.FromRgb(0x8A, 0x30, 0x06), // Rich Amber Orange 800
                new Point(0, 0),
                new Point(1, 1));

            double pad = s * 0.05;
            double bgSize = s * 0.90;
            double radius = s * 0.22;
            var bgRect = new Rect(pad, pad, bgSize, bgSize);

            // Viền ngoài phản quang sáng nhẹ ánh kim vàng/cam
            var borderPen = new Pen(new LinearGradientBrush(
                Color.FromArgb(180, 0xFB, 0xBF, 0x24), // Amber 400 Light Gold
                Color.FromArgb(70, 0x9A, 0x34, 0x12),  // Deep Amber Orange
                new Point(0, 0), new Point(1, 1)), s * 0.015);

            dc.DrawRoundedRectangle(bgBrush, borderPen, bgRect, radius, radius);

            // Vòng sáng nhẹ bên trong (Inner Glow) màu vàng hổ phách
            var innerGlowBrush = new LinearGradientBrush(
                Color.FromArgb(50, 0xFB, 0xBF, 0x24),
                Colors.Transparent,
                new Point(0.5, 0), new Point(0.5, 0.6));
            dc.DrawRoundedRectangle(innerGlowBrush, null,
                new Rect(pad + s * 0.01, pad + s * 0.01, bgSize - s * 0.02, bgSize * 0.45), radius * 0.9, radius * 0.9);

            // 2. MÀN HÌNH WORKSPACE / SERVER MONITOR
            double monX = s * 0.18;
            double monY = s * 0.18;
            double monW = s * 0.64;
            double monH = s * 0.48;
            double monRadius = s * 0.05;

            var monPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFD, 0xBA, 0x74)), s * 0.015); // Light Orange 300
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x03)), monPen, // Dark Amber Bronze
                new Rect(monX, monY, monW, monH), monRadius, monRadius);

            // Màn hình trong
            double scrX = monX + s * 0.03;
            double scrY = monY + s * 0.07;
            double scrW = monW - s * 0.06;
            double scrH = monH - s * 0.10;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x10, 0x0E)), null,
                new Rect(scrX, scrY, scrW, scrH), s * 0.02, s * 0.02);

            // 3 chấm tròn indicator trên thanh tiêu đề monitor
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)), null, new Point(monX + s * 0.05, monY + s * 0.035), s * 0.012, s * 0.012);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), null, new Point(monX + s * 0.08, monY + s * 0.035), s * 0.012, s * 0.012);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C)), null, new Point(monX + s * 0.11, monY + s * 0.035), s * 0.012, s * 0.012);

            // Nội dung bên trong màn hình: Các thanh server & data flow tông vàng/cam
            var linePen1 = new Pen(new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), s * 0.015); // Amber Gold
            dc.DrawLine(linePen1, new Point(scrX + s * 0.04, scrY + s * 0.06), new Point(scrX + scrW - s * 0.04, scrY + s * 0.06));

            var linePen2 = new Pen(new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C)), s * 0.015); // Warm Orange
            dc.DrawLine(linePen2, new Point(scrX + s * 0.04, scrY + s * 0.13), new Point(scrX + scrW * 0.65, scrY + s * 0.13));

            var linePen3 = new Pen(new SolidColorBrush(Color.FromRgb(0xFD, 0xE0, 0x47)), s * 0.015); // Yellow Bright
            dc.DrawLine(linePen3, new Point(scrX + s * 0.04, scrY + s * 0.20), new Point(scrX + scrW * 0.80, scrY + s * 0.20));

            // Biểu tượng Server Nodes (2 máy chủ kết nối)
            double srvW = s * 0.12;
            double srvH = s * 0.05;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x29, 0x1E, 0x17)), new Pen(new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), 1.5),
                new Rect(scrX + s * 0.04, scrY + s * 0.26, srvW, srvH), 2, 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x29, 0x1E, 0x17)), new Pen(new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)), 1.5),
                new Rect(scrX + s * 0.26, scrY + s * 0.26, srvW, srvH), 2, 2);

            // Đèn server
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), null, new Point(scrX + s * 0.06, scrY + s * 0.285), 2.5, 2.5);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)), null, new Point(scrX + s * 0.28, scrY + s * 0.285), 2.5, 2.5);

            // Đường nối kết nối giữa 2 server node
            var connPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)), s * 0.008);
            dc.DrawLine(connPen, new Point(scrX + s * 0.16, scrY + s * 0.285), new Point(scrX + s * 0.26, scrY + s * 0.285));

            // Chân đế Monitor
            var standBrush = new SolidColorBrush(Color.FromRgb(0x78, 0x71, 0x6C)); // Warm Stone
            dc.DrawRectangle(standBrush, null, new Rect(s * 0.46, monY + monH, s * 0.08, s * 0.06));
            dc.DrawRoundedRectangle(standBrush, null, new Rect(s * 0.38, monY + monH + s * 0.05, s * 0.24, s * 0.025), 3, 3);

            // 3. HUY HIỆU TỰ ĐỘNG HÓA & PLAY (AUTOMATION / PLAY BADGE)
            // Đặt ở góc dưới bên phải màn hình
            double badgeCenterX = s * 0.70;
            double badgeCenterY = s * 0.70;
            double badgeR = s * 0.18;

            // Đổ bóng huy hiệu
            var shadowBrush = new SolidColorBrush(Color.FromArgb(120, 0x43, 0x14, 0x07));
            dc.DrawEllipse(shadowBrush, null, new Point(badgeCenterX + s * 0.015, badgeCenterY + s * 0.02), badgeR, badgeR);

            // Vòng tròn huy hiệu tông Vàng - Cam rực rỡ Gradient
            var badgeGrad = new LinearGradientBrush(
                Color.FromRgb(0xFB, 0xBF, 0x24), // Amber 400 Light Gold
                Color.FromRgb(0xEA, 0x58, 0x0C), // Orange 600 Rich Fire Orange
                new Point(0, 0), new Point(1, 1));
            var badgePen = new Pen(new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A)), s * 0.02); // Yellow 200 Bright
            dc.DrawEllipse(badgeGrad, badgePen, new Point(badgeCenterX, badgeCenterY), badgeR, badgeR);

            // Biểu tượng tam giác Play màu trắng sắc nét ở giữa huy hiệu
            var playGeom = new StreamGeometry();
            using (var ctx = playGeom.Open())
            {
                double px = badgeCenterX - s * 0.04;
                double py = badgeCenterY - s * 0.07;
                double pw = s * 0.11;
                double ph = s * 0.14;

                ctx.BeginFigure(new Point(px, py), true, true);
                ctx.LineTo(new Point(px + pw, py + ph / 2.0), true, false);
                ctx.LineTo(new Point(px, py + ph), true, false);
            }
            playGeom.Freeze();
            dc.DrawGeometry(Brushes.White, null, playGeom);

            // Hiệu ứng tia sáng phản chiếu chéo trên huy hiệu
            var badgeGlow = new LinearGradientBrush(
                Color.FromArgb(90, 255, 255, 255),
                Colors.Transparent,
                new Point(0.2, 0), new Point(0.8, 1));
            dc.DrawEllipse(badgeGlow, null, new Point(badgeCenterX, badgeCenterY - badgeR * 0.3), badgeR * 0.7, badgeR * 0.4);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
    }

    static BitmapSource ResizeBitmap(BitmapSource source, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
    }
}
