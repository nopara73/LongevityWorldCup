interface BiomarkerExplorerMarker {
    readonly key: string;
    readonly label: string;
    readonly unit: string;
    readonly domain: string;
}

interface BiomarkerExplorerRecord {
    readonly id: string;
    readonly athleteId: string;
    readonly athleteName: string;
    readonly division: string;
    readonly date: string;
    readonly day: number;
    readonly age: number | null;
    readonly values: Readonly<Record<string, number>>;
    readonly pheno: number | null;
    readonly bortz: number | null;
    readonly phenoTerms: readonly BiomarkerExplorerTerm[];
    readonly bortzTerms: readonly BiomarkerExplorerTerm[];
}

interface BiomarkerExplorerTerm {
    readonly key: string;
    readonly label: string;
    readonly value: number;
    readonly raw: number;
    readonly scored: number;
    readonly unit: string;
    readonly transformed: boolean;
}

interface BiomarkerExplorerAthlete {
    readonly id: string;
    readonly name: string;
    readonly division: string;
    readonly records: readonly BiomarkerExplorerRecord[];
    readonly invalidDates: number;
}

interface BiomarkerExplorerCohort {
    readonly records: readonly BiomarkerExplorerRecord[];
    readonly ambiguous: number;
    readonly withoutRecords: number;
}

interface BiomarkerExplorerDataApi {
    readonly markers: readonly BiomarkerExplorerMarker[];
    dateDay(value: unknown): number | null;
    normalize(value: unknown): BiomarkerExplorerAthlete;
    latestCohort(athletes: readonly BiomarkerExplorerAthlete[], excludedId: string): BiomarkerExplorerCohort;
    ecdf(values: readonly number[]): readonly { x: number; y: number }[];
    counts(values: readonly number[], value: number): { lower: number; equal: number; higher: number; atOrBelow: number };
    standardization(values: readonly number[]): { mean: number; sd: number; n: number } | null;
    rankAssociation(pairs: readonly { x: number; y: number }[]): number | null;
    samePanelPairs(records: readonly BiomarkerExplorerRecord[], xKey: string, yKey: string): readonly { record: BiomarkerExplorerRecord; x: number; y: number }[];
    series(records: readonly BiomarkerExplorerRecord[], value: (record: BiomarkerExplorerRecord) => number | null): readonly { x: number; y: number | null; recordId: string | null }[];
    clockChange(a: BiomarkerExplorerRecord, b: BiomarkerExplorerRecord, clock: 'pheno' | 'bortz'): readonly { key: string; label: string; change: number; a: number; b: number; unit: string; capped: boolean; transformed: boolean }[] | null;
}

interface BiomarkerExplorerApi {
    mount(root: HTMLElement, athlete: unknown, field: readonly unknown[]): void;
    destroy(): void;
    resize(): void;
    refresh(): void;
}

interface Window {
    LwcBiomarkerData?: BiomarkerExplorerDataApi;
    LwcBiomarkerExplorer?: BiomarkerExplorerApi;
}
