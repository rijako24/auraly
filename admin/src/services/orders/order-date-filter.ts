export function localOrderDateValue(now = new Date()): string {
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
}

export function orderDayRange(value: string): {
  createdFrom: string;
  createdTo: string;
} {
  const start = new Date(`${value}T00:00:00`);
  const end = new Date(start);
  end.setDate(end.getDate() + 1);
  return {
    createdFrom: start.toISOString(),
    createdTo: end.toISOString(),
  };
}
