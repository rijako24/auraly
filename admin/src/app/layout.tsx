import type { Metadata, Viewport } from "next";

import { Toaster } from "@/components/ui/toaster";
import { ThemeProvider } from "@/providers/theme-provider";
import { QueryProvider } from "@/providers/query-provider";
import { PwaProvider } from "@/providers/pwa-provider";

import "./globals.css";


export const metadata: Metadata = {
  title: "Auraly | Admin",
  description: "Panel de administración de Auraly",
  manifest: "/app.webmanifest",
  icons: {
    icon: "/brand/auraly-app-icon-192.png",
    shortcut: "/brand/auraly-app-icon-192.png",
    apple: "/brand/auraly-app-icon-512.png",
  },
  appleWebApp: { capable: true, statusBarStyle: "default", title: "Auraly" },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  interactiveWidget: "resizes-content",
  themeColor: "#f8fafc",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="es" suppressHydrationWarning>
      <body className="font-sans antialiased">
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
