import type { Metadata, Viewport } from "next";

export const metadata: Metadata = {
  title: "Auraly POS",
  description: "Punto de venta local de Auraly Commerce",
  manifest: "/pos.webmanifest",
  icons: {
    icon: "/brand/auraly-mark.png",
    apple: "/brand/auraly-mark.png",
  },
  appleWebApp: {
    capable: true,
    statusBarStyle: "black-translucent",
    title: "Auraly POS",
  },
};

export const viewport: Viewport = {
  themeColor: "#07161A",
  width: "device-width",
  initialScale: 1,
};

export default function PosLayout({ children }: { children: React.ReactNode }) {
  return children;
}
