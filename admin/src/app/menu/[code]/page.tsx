import { PublicRestaurantMenu } from "@/components/restaurant/public-restaurant-menu";

export default async function RestaurantMenuPage({ params }: {
  params: Promise<{ code: string }>;
}) {
  const { code } = await params;
  return <PublicRestaurantMenu code={code}/>;
}
