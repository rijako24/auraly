import type { Metadata } from "next";

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
    icon: "/brand/auraly-mark.png",
    shortcut: "/brand/auraly-mark.png",
    apple: "/brand/auraly-mark.png",
  },
  appleWebApp: { capable: true, statusBarStyle: "black-translucent", title: "Auraly" },
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
