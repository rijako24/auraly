import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Auraly | Facturación, operación e inteligencia para tu empresa",
  description: "Conecta facturación electrónica, inventario, contabilidad, nómina, pedidos online y offline, entregas, recaudos y agentes de IA en una sola plataforma.",
};

export default function LandingLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="min-h-screen bg-background">
      {children}
    </div>
  );
}
