import { BusinessContextProvider } from "@/providers/business-context-provider";

export default function RestaurantLayout({ children }: { children: React.ReactNode }) {
  return <BusinessContextProvider>{children}</BusinessContextProvider>;
}
