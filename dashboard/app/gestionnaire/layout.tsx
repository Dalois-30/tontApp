import { Suspense } from "react";
import Sidebar, { RolePlateformeProvider } from "@/components/Sidebar";
import { getSession } from "@/lib/session";

export default async function Layout({ children }: { children: React.ReactNode }) {
  const session = await getSession();
  return (
    <RolePlateformeProvider role={session.role}>
    <div className="lg:flex min-h-screen">
      {/* The sidebar reads the chosen tontine from the URL (useSearchParams). */}
      <Suspense fallback={null}>
        <Sidebar role={session.role} nom={session.nom} />
      </Suspense>
      <main className="flex-1 min-w-0 px-4 md:px-8 py-6 md:py-8">
        <div className="mx-auto max-w-6xl space-y-6">{children}</div>
      </main>
    </div>
    </RolePlateformeProvider>
  );
}
