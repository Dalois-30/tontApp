import { Suspense } from "react";
import Sidebar from "@/components/Sidebar";
import { getSession } from "@/lib/session";

export default async function Layout({ children }: { children: React.ReactNode }) {
  const session = await getSession();
  return (
    <div className="lg:flex min-h-screen">
      <Suspense fallback={null}>
        <Sidebar role={session.role || "Admin"} nom={session.nom} />
      </Suspense>
      <main className="flex-1 min-w-0 px-4 md:px-8 py-6 md:py-8">
        <div className="mx-auto max-w-6xl space-y-6">{children}</div>
      </main>
    </div>
  );
}
