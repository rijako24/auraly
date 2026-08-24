import type { Metadata, Viewport } from "next";

export const metadata: Metadata = {
  title: "Auraly POS",
  description: "Punto de venta local de Auraly Commerce",
  manifest: "/pos.webmanifest",
  icons: {
    icon: "/brand/auraly-app-icon-192-v4.png?v=5",
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
