import type { Metadata, Viewport } from "next";

import { Toaster } from "@/components/ui/toaster";
import { ThemeProvider } from "@/providers/theme-provider";
import { QueryProvider } from "@/providers/query-provider";
import { PwaProvider } from "@/providers/pwa-provider";
import { AppBootScreen } from "@/components/pwa/app-boot-screen";

import "./globals.css";


export const metadata: Metadata = {
  title: "Auraly | Admin",
  description: "Panel de administración de Auraly",
  manifest: "/app.webmanifest",
  icons: {
    icon: "/brand/auraly-mark.png?v=7",
    shortcut: "/brand/auraly-mark.png?v=7",
    apple: "/brand/auraly-ios-icon-512-v4.png?v=5",
  },
  appleWebApp: {
    capable: true,
    statusBarStyle: "default",
    title: "Auraly",
    startupImage: [
      { url: "/brand/launch/auraly-750x1334-v4.png?v=5", media: "(device-width: 375px) and (device-height: 667px) and (-webkit-device-pixel-ratio: 2) and (orientation: portrait)" },
      { url: "/brand/launch/auraly-828x1792-v4.png?v=5", media: "(device-width: 414px) and (device-height: 896px) and (-webkit-device-pixel-ratio: 2) and (orientation: portrait)" },
      { url: "/brand/launch/auraly-1125x2436-v4.png?v=5", media: "(device-width: 375px) and (device-height: 812px) and (-webkit-device-pixel-ratio: 3) and (orientation: portrait)" },
      { url: "/brand/launch/auraly-1170x2532-v4.png?v=5", media: "(device-width: 390px) and (device-height: 844px) and (-webkit-device-pixel-ratio: 3) and (orientation: portrait)" },
      { url: "/brand/launch/auraly-1179x2556-v4.png?v=5", media: "(device-width: 393px) and (device-height: 852px) and (-webkit-device-pixel-ratio: 3) and (orientation: portrait)" },
      { url: "/brand/launch/auraly-1242x2688-v4.png?v=5", media: "(device-width: 414px) and (device-height: 896px) and (-webkit-device-pixel-ratio: 3) and (orientation: portrait)" },
      { url: "/brand/launch/auraly-1284x2778-v4.png?v=5", media: "(device-width: 428px) and (device-height: 926px) and (-webkit-device-pixel-ratio: 3) and (orientation: portrait)" },
      { url: "/brand/launch/auraly-1290x2796-v4.png?v=5", media: "(device-width: 430px) and (device-height: 932px) and (-webkit-device-pixel-ratio: 3) and (orientation: portrait)" },
    ],
  },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  interactiveWidget: "resizes-content",
  themeColor: "#f8fafc",
  colorScheme: "light",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="es" suppressHydrationWarning style={{ backgroundColor: "#f8fafc", colorScheme: "light" }}>
      <head>
        <meta name="supported-color-schemes" content="light" />
        <style>{`html,body,#auraly-standalone-boot{background:#f8fafc!important;color-scheme:light}`}</style>
      </head>
      <body className="font-sans antialiased" style={{ backgroundColor: "#f8fafc" }}>
        <div id="auraly-standalone-boot"><AppBootScreen /></div>
        <ThemeProvider
          attribute="class"
          defaultTheme="light"
          enableSystem={false}
          disableTransitionOnChange
        >
          <QueryProvider>
            <PwaProvider>{children}<Toaster /></PwaProvider>
          </QueryProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
