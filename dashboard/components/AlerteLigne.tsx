import { formatDate } from "@/lib/format";
import type { AlerteDto } from "@/lib/types";
import { Badge } from "./ui";

export function AlerteLigne({ a }: { a: AlerteDto }) {
  return (
    <li className="flex items-start gap-4 px-5 md:px-6 py-3.5">
      <Badge status={a.severity} />
      <div className="min-w-0 flex-1">
        <p className="text-ink">{a.message}</p>
        <p className="text-[12px] text-muted">{a.type} · {formatDate(a.timestamp)}</p>
      </div>
    </li>
  );
}

