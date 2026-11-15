import { Badge } from "./ui";

/** Kept for existing imports: status badge with icon + French label. */
export default function StatusBadge({ status }: { status: string }) {
  return <Badge status={status} />;
}
