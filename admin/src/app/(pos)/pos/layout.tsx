import type { Metadata, Viewport } from "next";

export const metadata: Metadata = {
  title: "Auraly",
  description: "Punto de venta de Auraly",
  manifest: "/pos.webmanifest",
  icons: {
    icon: "/brand/auraly-icon-v5.png?v=6",
    apple: "/brand/auraly-ios-icon-512-v4.png?v=5",
  },
};

export const viewport: Viewport = {
  themeColor: "#f8fafc",
  width: "device-width",
  initialScale: 1,
};

export default function PosLayout({ children }: { children: React.ReactNode }) {
  return children;
}
