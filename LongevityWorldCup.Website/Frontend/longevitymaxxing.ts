(function () {
    type HabitKey = "sleep" | "exercise" | "nutrition" | "vices";
    type IdentityScope = "signup" | "edit";
    type IdentityMode = "participant" | "athlete";
    type ParticipantTab = "checkin" | "profile" | "home";
    type AccessTab = "signup" | "signin";
    type QuoteBucket = HabitKey | "mindset";
    type ButtonWork = () => Promise<void>;
    type ErrorHandler = (error: unknown) => void;
    type Quote = readonly [text: string, author: string, athleteSlug: string, sourceUrl: string];

    interface AnswerOption {
        label: string;
        value: number;
    }

    interface ChallengeQuestion {
        key: HabitKey;
        icon: string;
        title: string;
        text: string;
    }

    interface DaySummary {
        challengeDay: number;
        date: string;
    }

    interface DayCell {
        challengeDay: number;
        checkedIn: boolean;
        score: number | null;
        countsForScore: boolean;
        sleep: number | null;
        exercise: number | null;
        nutrition: number | null;
        vices: number | null;
    }

    interface DashboardCell extends DayCell {
        date: string;
    }

    interface CheckInImage {
        url: string;
        width: number;
        height: number;
    }

    interface CheckInDraft {
        sleep: number;
        exercise: number;
        nutrition: number;
        vices: number;
        note: string | null;
        images: CheckInImage[];
    }

    interface CheckInFormDraft {
        sleep: number;
        exercise: number;
        nutrition: number;
        vices: number;
        note: string;
    }

    interface CheckInPayload {
        accessToken: string;
        challengeDay: number;
        sleep: number;
        exercise: number;
        nutrition: number;
        vices: number;
        note: string | null;
    }

    interface EligibleDay {
        challengeDay: number;
        date: string;
        countsForScore: boolean;
        existing: CheckInDraft | null;
    }

    interface ParticipantNote {
        participantId: string;
        displayName: string;
        challengeDay: number;
        date: string;
        note: string | null;
        updatedAtUtc: string;
        images: CheckInImage[];
    }

    interface LeaderboardRow {
        participantId: string;
        displayName: string;
        athleteUrl: string | null;
        profileImageUrl: string | null;
        checkedInDays: number;
        totalPoints: number;
        currentStreak: number;
        cells: DayCell[];
        badges: string[];
        latestCheckInAtUtc: string | null;
        challengeEmailsStopped: boolean;
        challengeInactive: boolean;
        commitmentStatus: string | null;
    }

    interface PodiumRow {
        placement: number;
        displayName: string;
        athleteUrl: string | null;
        profileImageUrl: string | null;
        checkedInDays: number;
        totalPoints: number;
    }

    interface CallSlot {
        id: string;
        startsAtUtc: string;
    }

    interface PublicCall {
        key: string;
        label: string;
        candidateSlots: CallSlot[];
        selectedSlot: CallSlot | null;
    }

    interface ParticipantCall {
        key: string;
        label: string;
        selectedSlot: CallSlot | null;
        videoCallUrl: string | null;
    }

    interface PublicState {
        challengeName: string;
        phase: string;
        signupOpen: boolean;
        startDate: string;
        signupClosesAtUtc: string;
        callSelectionClosesAtUtc: string;
        endDate: string;
        durationDays: number;
        dailyMaxScore: number;
        days: DaySummary[];
        leaderboard: LeaderboardRow[];
        podium: PodiumRow[];
        notes: ParticipantNote[];
        calls: PublicCall[];
        slackInviteUrl: string;
        slackRoomUrl: string | null;
    }

    interface ParticipantSummary {
        id: string;
        email: string;
        displayName: string;
        timeZoneId: string;
        athleteSlug: string | null;
        athleteUrl: string | null;
        profileImageUrl: string | null;
        challengeEmailsStopped: boolean;
        challengeInactive: boolean;
        commitmentAmountUsd: number | null;
        daysIn: number;
    }

    interface CommitmentState {
        status: string;
        blocksParticipant: boolean;
        canEditAmount: boolean;
        canPay: boolean;
        amountUsd: number | null;
        owedAmountUsd: number | null;
        triggerChallengeDay: number | null;
        triggerScore: number | null;
        thresholdAverage: number | null;
        invoiceId: string | null;
        checkoutLink: string | null;
        invoiceStatus: string | null;
        message: string | null;
    }

    interface CommitmentTrendGuidance {
        enforced: boolean;
        priorScoredDays: number;
        averagePoints: number | null;
        neededPoints: number | null;
        text: string;
    }

    interface ParticipantState {
        public: PublicState;
        participant: ParticipantSummary;
        eligibleDays: EligibleDay[];
        notes: ParticipantNote[];
        calls: ParticipantCall[];
        commitment: CommitmentState;
        trendGuidance: CommitmentTrendGuidance;
    }

    interface SignupResult {
        message: string;
    }

    interface AccessResult {
        accessToken: string;
        state: ParticipantState;
    }

    interface ParticipantNotice {
        message: string;
        isError: boolean;
    }

    interface ResponsiveLabel {
        shortLabel: string;
        longLabel: string;
    }

    interface DashboardCategory {
        key: HabitKey;
        label: string;
        icon: string;
        tone: string;
    }

    interface CategorySummary {
        category: DashboardCategory;
        total: number;
        max: number;
        rate: number;
    }

    interface HabitBreakdownItem {
        key: HabitKey;
        label: string;
        short: string;
        value: number;
    }

    interface AthleteOption {
        name: string;
        legalName: string;
        slug: string;
        profilePic: string;
    }

    interface AthleteSelectorController {
        input: HTMLInputElement;
        athletes: AthleteOption[];
        clear: () => void;
        getPayload: () => string | null;
        getSelectedName: () => string;
        setValue: (value: string) => void;
    }

    interface DateOfBirthParts {
        Year: number;
        Month: number;
        Day: number;
    }

    interface BiomarkerEntry {
        Date: string;
        Wbc1000cellsuL: number | null;
        LymPc: number | null;
        NeutrophilPc: number | null;
        MonocytePc: number | null;
        Rbc10e12L: number | null;
        McvFL: number | null;
        MchPg: number | null;
        RdwPc: number | null;
        AlbGL: number | null;
        AltUL: number | null;
        AlpUL: number | null;
        GgtUL: number | null;
        UreaMmolL: number | null;
        CreatUmolL: number | null;
        CystatinCMgL: number | null;
        GluMmolL: number | null;
        Hba1cMmolMol: number | null;
        CholesterolMmolL: number | null;
        ApoA1GL: number | null;
        CrpMgL: number | null;
        ShbgNmolL: number | null;
        VitaminDNmolL: number | null;
    }

    interface CompletePhenoBiomarkerEntry extends BiomarkerEntry {
        Wbc1000cellsuL: number;
        LymPc: number;
        McvFL: number;
        RdwPc: number;
        AlbGL: number;
        AlpUL: number;
        CreatUmolL: number;
        GluMmolL: number;
        CrpMgL: number;
    }

    interface CompleteBortzBiomarkerEntry extends CompletePhenoBiomarkerEntry {
        NeutrophilPc: number;
        MonocytePc: number;
        Rbc10e12L: number;
        MchPg: number;
        AltUL: number;
        GgtUL: number;
        UreaMmolL: number;
        CystatinCMgL: number;
        Hba1cMmolMol: number;
        CholesterolMmolL: number;
        ApoA1GL: number;
        ShbgNmolL: number;
        VitaminDNmolL: number;
    }

    interface AthleteRecord {
        Name?: string;
        DisplayName?: string;
        AthleteSlug?: string;
        ProfilePic?: string;
        ProfilePicLeaderboardThumb?: string;
        ProfilePicThumb?: string;
        DateOfBirth?: DateOfBirthParts;
        Biomarkers?: BiomarkerEntry[];
        Division?: string;
        ExclusiveLeague?: string | null;
        CrowdAge?: number | null;
        CrowdCount?: number | null;
        PodcastLink?: string | null;
        PhenoAgeImprovementFromWorst?: number | null;
        BortzAgeImprovementFromWorst?: number | null;
    }

    interface QuoteAthlete {
        name: string;
        legalName: string;
        slug: string;
        profilePic: string;
        dateOfBirth: Date;
        chronologicalAge: number;
        crowdAge: number | null;
        generation: string;
        division: string;
        exclusiveLeague: string;
        podcastLink: string;
        rank?: number | null;
        ageReduction: number | null;
        ageReductionPercent: number | null;
        lowestPhenoAge: number;
        chronoAtLowestPhenoAge: number;
        bortzAgeReduction: number | null;
        lowestBortzAge: number | null;
        chronoAtLowestBortzAge: number | null;
        phenoAgeImprovement: number | null;
        bortzAgeImprovement: number | null;
        crowdAgeReduction: number | null;
        crowdCount: number;
        bestRankCandidates: QuoteRankCandidate[];
    }

    interface QuoteRankCandidate {
        key: string;
        rank: number;
        leagueName: string;
        leagueLabel: string;
        leagueLabelHtml: string | null;
        leagueType: string;
        href: string | null;
        targetBlank: boolean;
        tiePriority: number;
    }

    interface QuoteRankCandidateInput {
        rank?: number | null;
        leagueName?: string | null;
        leagueLabel?: string | null;
        leagueLabelHtml?: string | null;
        leagueType?: string | null;
        href?: string | null;
        targetBlank?: boolean;
        tiePriority?: number;
    }

    interface QuoteSubmissionAge {
        submittedAt: Date;
        index: number;
    }

    interface QuotePhenoStats {
        lowestPhenoAge: number;
        chronoAtLowestPhenoAge: number;
        ageReduction: number | null;
        ageReductionPercent: number | null;
        phenoAgeImprovement: number | null;
    }

    interface QuoteBortzStats {
        lowestBortzAge: number | null;
        chronoAtLowestBortzAge: number | null;
        bortzAgeReduction: number | null;
        bortzAgeImprovement: number | null;
    }

    interface CheckInQuote {
        bucket: QuoteBucket;
        text: string;
        athleteName: string;
        athleteSlug: string;
        youtubeUrl: string;
    }

    interface PhenoAgeApi {
        calculatePhenoAge(values: number[]): number;
    }

    interface BortzAgeApi {
        calculateBortzAge(age: number, values: number[]): number;
    }

    interface SharedWindowApi {
        PhenoAge?: PhenoAgeApi;
        BortzAge?: BortzAgeApi;
        calculateAgeAtDate?: (birthDate: Date, atDate: Date) => number;
        getGeneration?: (birthYear: number) => string;
        slugifyName?: (name: string, encode?: boolean) => string;
    }

    interface CommitmentPaymentCheckOptions {
        showWaiting?: boolean;
        finalWaitingMessage?: string;
    }

    interface RefreshPublicOptions {
        keepParticipant?: boolean;
    }

    const STORAGE_KEY = "lmxAccessToken";
    const API = "/api/longevitymaxxing";
    const REQUEST_TIMEOUT_MS = 65000;
    const CALL_ACTIVE_WINDOW_MS = 90 * 60 * 1000;
    const ANSWERS: readonly AnswerOption[] = [
        { label: "No", value: 0 },
        { label: "Somewhat", value: 1 },
        { label: "Yes", value: 2 }
    ];
    const SAVED_CHECKIN_TEXT = "Saved. You can edit this check-in today.";
    const MAX_NOTE_PHOTOS = 4;
    const RECENT_REMARK_LIMIT = 3;
    const NOTE_PHOTO_MAX_DIMENSION = 1600;
    const LEADERBOARD_SCORING_WINDOW_DAYS = 14;
    const COMMITMENT_PAYMENT_POLL_DELAYS_MS = [2500, 5000, 8000, 12000];
    const QUESTIONS: readonly ChallengeQuestion[] = [
        { key: "sleep", icon: "fa-moon", title: "Sleep", text: "Did you set yourself up for good sleep last night?" },
        { key: "exercise", icon: "fa-dumbbell", title: "Exercise", text: "Did you challenge or intentionally rest your body yesterday?" },
        { key: "nutrition", icon: "fa-bowl-food", title: "Nutrition", text: "By your own standards, did you eat healthy yesterday?" },
        { key: "vices", icon: "fa-shield-halved", title: "Vices", text: "Were your vices under control yesterday?" }
    ];
    const LMX_QUOTES: Record<QuoteBucket, readonly Quote[]> = {
        sleep: [
            ["Sleep is still a work in progress, immensely better than it's been. Going to sleep at the same time every night, trying to sleep as much as I physically can, that is almost a constant.","Michael Lustgarten","michael_lustgarten","https://youtu.be/KFfGdf20-1g"],
            ["Sleep quality is immeasurably better, and nothing affects my mental health more than a great night of sleep.","Michael Lustgarten","michael_lustgarten","https://youtu.be/KFfGdf20-1g"],
            ["Your day obviously starts with what goes on the night before. Be bored, do nothing. No screens, no bright lights. Wind down properly.","Wen Z","wen_z","https://www.youtube.com/watch?v=Bf6c7JE5xX4"],
            ["Getting good sleep and enough sleep will literally make you way more efficient the next day and every other day after that.","Wen Z","wen_z","https://www.youtube.com/watch?v=Bf6c7JE5xX4"],
            ["Sleep is probably the weakest part of my approach, but I try to be very consistent with it.","Wen Z","wen_z","https://www.youtube.com/watch?v=Bf6c7JE5xX4"],
            ["The single most effective thing for my sleep actually was the morning walk: starting the walk first thing in the morning to get the sunlight and get the exercise.","Inka Land","inka_land","https://www.youtube.com/watch?v=KP880OKbYrw"],
            ["The morning walk is a very important habit for the early day energy levels that I have. It has increased that a lot.","Inka Land","inka_land","https://www.youtube.com/watch?v=KP880OKbYrw"],
            ["As regular as possible is challenging when there's travel involved or social engagements, but one of the most important parts of sleep hygiene now is to keep that regular bedtime.","HealthOptimisers","healthoptimisers","https://youtu.be/Iwno9u6AHxs"],
            ["I'd love people to give as much focus to sleep as I have learnt to do. That doesn't need to be with any tools, but to have a sleep routine.","HealthOptimisers","healthoptimisers","https://youtu.be/Iwno9u6AHxs"],
            ["For me, the focus to sleep is something that should not be underestimated compared to nutrition and all these other things.","HealthOptimisers","healthoptimisers","https://youtu.be/Iwno9u6AHxs"],
            ["When I get my pattern down and I'm consistent, same time every night, I sleep like a rock.","Richard Heck","richard_heck","https://youtu.be/RaEUPU1Oej4"],
            ["My sleep schedule is pretty basic. It's just the same time every night, and perfect darkness is really nice.","Richard Heck","richard_heck","https://youtu.be/RaEUPU1Oej4"],
            ["I definitely like the idea of separating from a device in the evening. If I'm listening to a simple story, I usually will just go right to sleep.","Cher","cher","https://youtu.be/H0kWwC_z2v0"],
            ["I had a long story of trying to fix a broken sleep. Right now, I finally fixed my sleep.","Max","max","https://www.youtube.com/watch?v=P0H3XvBUte0"],
            ["I like to have good quality sleep, but I know that it also fluctuates. I just try to optimize as best as I can.","Ilhui","ilhui","https://www.youtube.com/watch?v=qFWWghmFSCc"]
        ],
        exercise: [
            ["I had to put in the work: working out, having a clean diet, getting rid of all the bad things in my life that had been a burden on my body. That allowed me to actually excel.","Keith Blondin","keith_blondin","https://youtu.be/hNpXAXH9bT0"],
            ["I didn't have any equipment. I was just doing what I could. Then I started seeing some results and just kept building upon that.","Keith Blondin","keith_blondin","https://youtu.be/hNpXAXH9bT0"],
            ["I think the biggest one is just move. Don't just sit all the time. Don't give an excuse that you're at your desk or you're too busy.","Mel C","mel_c","https://youtu.be/4hFzzMfV2To"],
            ["It doesn't have to be a big movement to start with. All these small steps count: walking a little more, taking the stairs a little bit.","Mel C","mel_c","https://youtu.be/4hFzzMfV2To"],
            ["Fifteen minutes would still make a difference rather than zero minutes. To me, movement is very important.","Mel C","mel_c","https://youtu.be/4hFzzMfV2To"],
            ["You can get a pair of shoes, step outside, and start running. You don't need to go on a health retreat. You don't need to pay thousands of dollars.","HealthOptimisers","healthoptimisers","https://youtu.be/Iwno9u6AHxs"],
            ["Movement is something that I have done every decade of my life. It's just the most consistent thing that I've always done.","Cher","cher","https://youtu.be/H0kWwC_z2v0"],
            ["I don't really have a day off where I do absolutely nothing. I feel like it's good to just kind of move every day.","Richard Heck","richard_heck","https://youtu.be/RaEUPU1Oej4"],
            ["The first and very important principle is consistency. I have to consistently work out, and for five years I did not skip a single workout.","Philipp Schmeing","philipp_schmeing","https://youtu.be/2V-TPK4Ni0g"],
            ["If you don't have this competitive mindset against yourself a week ago or a month ago, maybe at one point you stop working out altogether.","Philipp Schmeing","philipp_schmeing","https://youtu.be/2V-TPK4Ni0g"],
            ["If you start working out, it is a good idea to do one set less than to injure yourself.","Philipp Schmeing","philipp_schmeing","https://youtu.be/2V-TPK4Ni0g"],
            ["I went ahead for 40 or 50 days with zero equipment, using plastic water bottles for dumbbells.","Zdenek Sipek","zdenek_sipek","https://youtu.be/Ma13R7YRcho"],
            ["It doesn't matter where you come from, what your condition is, or what your age is. You always have an opportunity to be the best version of yourself.","Amandeep","amandeep","https://youtu.be/Wz0nElWOwO0"],
            ["You don't necessarily have to do fast movements to stress out your heart.","Amandeep","amandeep","https://youtu.be/Wz0nElWOwO0"],
            ["I do it to 70% of capacity. I reserve 30% of my energy, which I then spread out through the rest of my day.","Amandeep","amandeep","https://youtu.be/Wz0nElWOwO0"],
            ["If you continue doing the exercise as long as you can, it's maybe more important: the consistency of just having a reserve of it.","Ricardo di Lazzaro Filho","ricardo_di_lazzaro_filho","https://www.youtube.com/watch?v=AMsDDM76wXg"],
            ["I started an exercise routine by doing five minutes, three times a week, and I slowly built it up.","Richard Heck","richard_heck","https://youtu.be/RaEUPU1Oej4"],
            ["If your habits take too much willpower, they're probably not going to be long-term sustainable. Keep it in a comfortable range, something attainable for long periods of time.","Angela Buzzeo","angela_buzzeo","https://youtu.be/AY_ZgRqApCE"]
        ],
        nutrition: [
            ["I don't eat perfect at all, but there's a lot of thought that goes into it. Pretty much everything I eat is what I make and pick and hand select.","Cher","cher","https://youtu.be/H0kWwC_z2v0"],
            ["My espresso is like a treat. I believe there's space for having what you love.","Cher","cher","https://youtu.be/H0kWwC_z2v0"],
            ["I'm not going to totally deny myself, but I have to control that to work on the body composition I seek to have and maintain.","Cher","cher","https://youtu.be/H0kWwC_z2v0"],
            ["If I'm going to eat something like that, I'm going to eat something really good. If you're going to eat bad food, limit it to a special occasion.","Richard Heck","richard_heck","https://youtu.be/RaEUPU1Oej4"],
            ["I've eaten so much good food in the day. If I ate some bad food now, it's not that big of a deal.","Richard Heck","richard_heck","https://youtu.be/RaEUPU1Oej4"],
            ["I don't subscribe to the idea that you need to have a 100% strict diet, at least not all the time.","Siim Land","siim_land","https://www.youtube.com/watch?v=ogy7l0nka-Y"],
            ["You can make a healthy diet work even with small amounts of what people would consider unhealthy foods.","Siim Land","siim_land","https://www.youtube.com/watch?v=ogy7l0nka-Y"],
            ["I believe in flexibility, and I don't believe that rigid lifestyles can help you to a sustainable outcome long term.","Ilhui","ilhui","https://www.youtube.com/watch?v=qFWWghmFSCc"],
            ["It's the sum of everything: these consistent habits that you have for years, all these little habits that become part of your lifestyle.","Ilhui","ilhui","https://www.youtube.com/watch?v=qFWWghmFSCc"],
            ["Whole foods, tasty foods, not processed foods, and no sugar. At some point your body realizes: this is good for me.","Philipp Schmeing","philipp_schmeing","https://youtu.be/2V-TPK4Ni0g"],
            ["Every weekday I'm eating the same thing, but I'm always looking forward to it because for me it tastes great.","Philipp Schmeing","philipp_schmeing","https://youtu.be/2V-TPK4Ni0g"],
            ["You think if you don't do 110% you die. It's not true. If you do an 80% approach, you're already fine.","Philipp Schmeing","philipp_schmeing","https://youtu.be/2V-TPK4Ni0g"],
            ["I have a backup strategy where I don't just jump all the way to hot dogs and pizza, but gradually make things easier to eat without sacrificing too much.","Max","max","https://www.youtube.com/watch?v=P0H3XvBUte0"],
            ["Find rewards that create minor trade-offs, but actually help you keep going long-term, so you don't relapse.","Max","max","https://www.youtube.com/watch?v=P0H3XvBUte0"],
            ["There are options where you can choose to be healthier. It's just making the right choices.","Mel C","mel_c","https://youtu.be/4hFzzMfV2To"],
            ["If you do it once in a while, it's okay as long as you don't do it all the time.","Mel C","mel_c","https://youtu.be/4hFzzMfV2To"],
            ["If you don't understand anything in the label, drop it. It has to be whole foods, or the label has to be understandable.","Amandeep","amandeep","https://youtu.be/Wz0nElWOwO0"]
        ],
        vices: [
            ["The wrong lifestyle decisions I've made were my decisions. Temptations are temptations, but nothing feels better than feeling healthy.","Wen Z","wen_z","https://www.youtube.com/watch?v=Bf6c7JE5xX4"],
            ["Nothing feels better than waking up and not feeling groggy, not wanting to doom scroll in bed for two hours, and having no fatigue during the day.","Wen Z","wen_z","https://www.youtube.com/watch?v=Bf6c7JE5xX4"],
            ["When I started doing more sports and more work, you see you are not that efficient in your daily life and you are not performant the next day.","Philipp Schmeing","philipp_schmeing","https://youtu.be/2V-TPK4Ni0g"],
            ["At some point we realized it's not worth it to have the hangover the next day. The next day is gone.","Philipp Schmeing","philipp_schmeing","https://youtu.be/2V-TPK4Ni0g"],
            ["If I had one drink, my HRV goes from the 120s to the 50s or 60s. It's a terrible night's sleep for me.","Richard Heck","richard_heck","https://youtu.be/RaEUPU1Oej4"],
            ["Alcohol is one of the things I've given up over time because I could see it in the data that it's not good for me.","Richard Heck","richard_heck","https://youtu.be/RaEUPU1Oej4"],
            ["The things they have in common are the things that they don't do. It's the things that they remove.","Dave Pascoe","dave_pascoe","https://youtu.be/b3D1k1-w9K4"],
            ["Our pace of aging is really based on what we remove rather than what we're adding or what we're doing.","Dave Pascoe","dave_pascoe","https://youtu.be/b3D1k1-w9K4"],
            ["Getting rid of all the bad things in my life that had been a burden on my body allowed me to actually excel.","Keith Blondin","keith_blondin","https://youtu.be/hNpXAXH9bT0"],
            ["I have my system and identity going with it. I don't really think about exercising, eating right, junk food, or alcohol. I am in control now.","Zdenek Sipek","zdenek_sipek","https://youtu.be/Ma13R7YRcho"],
            ["So I guess junk food is a vice, but I've learned to minimize it. I wouldn't say conquer it; I've learned to manage it.","Michael Lustgarten","michael_lustgarten","https://youtu.be/KFfGdf20-1g"],
            ["If I go completely cold turkey with any junk, it sets me up for one of these binges, and I don't want that. That's the antithesis of optimal health.","Michael Lustgarten","michael_lustgarten","https://youtu.be/KFfGdf20-1g"],
            ["How do you get off any kind of addiction? You find something better to do.","Richard Heck","richard_heck","https://youtu.be/RaEUPU1Oej4"],
            ["Find rewards that create minor trade-offs, but actually help you keep going long-term, so you don't relapse.","Max","max","https://www.youtube.com/watch?v=P0H3XvBUte0"]
        ],
        mindset: [
            ["If there is a competitive spirit in you, it never gives up. It always bites back. Once you get bitten by it, it always stays there.","Amandeep","amandeep","https://youtu.be/Wz0nElWOwO0"],
            ["I hope this story inspires folks who think, because of a chronic condition, life is over. That's not the case. You always have an opportunity.","Amandeep","amandeep","https://youtu.be/Wz0nElWOwO0"],
            ["If you do not take away money from longevity, you take an asset even more important than money, which is health.","Amandeep","amandeep","https://youtu.be/Wz0nElWOwO0"],
            ["There's no shortcut with health. It's a long-term game. It's not a sprint; it's a marathon.","Amandeep","amandeep","https://youtu.be/Wz0nElWOwO0"],
            ["Life is not about perfection. It's about probability. There's nothing certain about life; what we want is to improve the probability.","Amandeep","amandeep","https://youtu.be/Wz0nElWOwO0"],
            ["If your habits take too much willpower, they're probably not going to be long-term sustainable. Keep it in a comfortable range, something attainable for long periods of time.","Angela Buzzeo","angela_buzzeo","https://youtu.be/AY_ZgRqApCE"],
            ["It's all a habit and natural, so it's not willpower.","Angela Buzzeo","angela_buzzeo","https://youtu.be/AY_ZgRqApCE"],
            ["I realized it was just time now to get back to doing those healthier habits again. So I did.","Dave Pascoe","dave_pascoe","https://youtu.be/b3D1k1-w9K4"],
            ["What's the point of longevity if you're not living your life?","Dave Pascoe","dave_pascoe","https://youtu.be/b3D1k1-w9K4"],
            ["Someday you're going to be older. When you get there, are you going to be where you want to be? Do you have a purpose? Are you going towards something?","Dave Pascoe","dave_pascoe","https://youtu.be/b3D1k1-w9K4"],
            ["Discipline brings freedom. For the most part, I'm living fairly disciplined.","HealthOptimisers","healthoptimisers","https://youtu.be/Iwno9u6AHxs"],
            ["I believe in friends, family, connection, enjoying myself, growing, learning, and making mistakes as well. That's all part of the journey.","HealthOptimisers","healthoptimisers","https://youtu.be/Iwno9u6AHxs"],
            ["Health optimization comes back to the basics: exercise, sleep, nutrition, recovery.","HealthOptimisers","healthoptimisers","https://youtu.be/Iwno9u6AHxs"],
            ["If we see stress as an opportunity for growth, it comes back to mindset: learning how to cope with challenges can make us more resilient.","Ilhui","ilhui","https://www.youtube.com/watch?v=qFWWghmFSCc"],
            ["It's the approach on how we tackle challenges that can help us keep waking up and doing things we know are going to help us keep going.","Ilhui","ilhui","https://www.youtube.com/watch?v=qFWWghmFSCc"],
            ["It's just these little things that I do every day that are part of a lifestyle already.","Ilhui","ilhui","https://www.youtube.com/watch?v=qFWWghmFSCc"],
            ["I do believe in flexibility, and I don't believe rigid lifestyles can help you to a sustainable outcome long term.","Ilhui","ilhui","https://www.youtube.com/watch?v=qFWWghmFSCc"],
            ["What I do on a day-by-day basis really makes me feel good. I don't think it's perfect by any means; it's just the habits that I enjoy doing.","Inka Land","inka_land","https://www.youtube.com/watch?v=KP880OKbYrw"],
            ["I started meditation. I started changing my diet. I started reading the science myself. I started applying, and gradually, little by little, I tested a lot of habits.","Inka Land","inka_land","https://www.youtube.com/watch?v=KP880OKbYrw"],
            ["Gradually, I started identifying triggers and causes, and I was able to find a way of life that wouldn't cause me migraines.","Inka Land","inka_land","https://www.youtube.com/watch?v=KP880OKbYrw"],
            ["I realized how many things in the world can be changed by a lifestyle.","Inka Land","inka_land","https://www.youtube.com/watch?v=KP880OKbYrw"],
            ["This is a journey. Time passes and you keep learning and changing things and evolving.","Juan Robalino","juan_robalino","https://youtu.be/mYi8JlEWDYI"],
            ["What is difficult is to be consistently lower. You have to have good habits as a lifestyle. It's not that you do it for one day and one week and that's it.","Juan Robalino","juan_robalino","https://youtu.be/mYi8JlEWDYI"],
            ["I didn't really start this health journey until I hit 55.","Keith Blondin","keith_blondin","https://youtu.be/hNpXAXH9bT0"],
            ["I'm just working on my health and making myself the best possible.","Keith Blondin","keith_blondin","https://youtu.be/hNpXAXH9bT0"],
            ["Everybody wins because we're all getting better.","Keith Blondin","keith_blondin","https://youtu.be/hNpXAXH9bT0"],
            ["The goal is to do it for the rest of your life, however long you will live.","Markus Mattiasson","markus_mattiasson","https://www.youtube.com/watch?v=RDg2T_ypNDE"],
            ["A lot can be achieved with relatively simple means through just diet and lifestyle.","Markus Mattiasson","markus_mattiasson","https://www.youtube.com/watch?v=RDg2T_ypNDE"],
            ["Resilience is the most important thing, and avoiding frailty is what we want to pursue.","Markus Mattiasson","markus_mattiasson","https://www.youtube.com/watch?v=RDg2T_ypNDE"],
            ["Execution is taking time. Every time I adjust my routine or diet, I need to learn a new cooking routine or fitness routine. That adjustment takes effort and willpower.","Max","max","https://www.youtube.com/watch?v=P0H3XvBUte0"],
            ["Find rewards that help you keep going long term, so you don't relapse.","Max","max","https://www.youtube.com/watch?v=P0H3XvBUte0"],
            ["The mindset is critical. We shouldn't focus just on the body. Identify with the goal, so you are a longevity athlete.","Zdenek Sipek","zdenek_sipek","https://youtu.be/Ma13R7YRcho"]
        ]
    };
    const QUOTE_BUCKETS = ["sleep", "exercise", "nutrition", "vices", "mindset"];
    const CROWD_AGE_LEADERBOARD_MINIMUM_GUESS_COUNT = 100;
    const ATHLETE_PLACEHOLDER_IMAGE = "/assets/content-images/headshot.webp";
    const FALLBACK_TIME_ZONES = [
        "UTC",
        "Europe/London",
        "Europe/Berlin",
        "Europe/Budapest",
        "Europe/Athens",
        "Asia/Jerusalem",
        "Asia/Dubai",
        "Asia/Kolkata",
        "Asia/Bangkok",
        "Asia/Singapore",
        "Asia/Tokyo",
        "Australia/Sydney",
        "Pacific/Auckland",
        "America/St_Johns",
        "America/Halifax",
        "America/New_York",
        "America/Chicago",
        "America/Denver",
        "America/Los_Angeles",
        "America/Anchorage",
        "Pacific/Honolulu",
        "America/Mexico_City",
        "America/Bogota",
        "America/Lima",
        "America/Sao_Paulo",
        "America/Argentina/Buenos_Aires"
    ];
    const TIME_ZONE_COUNTRY_DATA = "Europe/Andorra=AD|Asia/Dubai=AE,OM,RE,SC,TF|Asia/Kabul=AF|Europe/Tirane=AL|Asia/Yerevan=AM|Antarctica/Casey=AQ|Antarctica/Davis=AQ|Antarctica/Mawson=AQ|Antarctica/Palmer=AQ|Antarctica/Rothera=AQ|Antarctica/Troll=AQ|Antarctica/Vostok=AQ|America/Argentina/Buenos_Aires=AR|America/Argentina/Cordoba=AR|America/Argentina/Salta=AR|America/Argentina/Jujuy=AR|America/Argentina/Tucuman=AR|America/Argentina/Catamarca=AR|America/Argentina/La_Rioja=AR|America/Argentina/San_Juan=AR|America/Argentina/Mendoza=AR|America/Argentina/San_Luis=AR|America/Argentina/Rio_Gallegos=AR|America/Argentina/Ushuaia=AR|Pacific/Pago_Pago=AS,UM|Europe/Vienna=AT|Australia/Lord_Howe=AU|Antarctica/Macquarie=AU|Australia/Hobart=AU|Australia/Melbourne=AU|Australia/Sydney=AU|Australia/Broken_Hill=AU|Australia/Brisbane=AU|Australia/Lindeman=AU|Australia/Adelaide=AU|Australia/Darwin=AU|Australia/Perth=AU|Australia/Eucla=AU|Asia/Baku=AZ|America/Barbados=BB|Asia/Dhaka=BD|Europe/Brussels=BE,LU,NL|Europe/Sofia=BG|Atlantic/Bermuda=BM|America/La_Paz=BO|America/Noronha=BR|America/Belem=BR|America/Fortaleza=BR|America/Recife=BR|America/Araguaina=BR|America/Maceio=BR|America/Bahia=BR|America/Sao_Paulo=BR|America/Campo_Grande=BR|America/Cuiaba=BR|America/Santarem=BR|America/Porto_Velho=BR|America/Boa_Vista=BR|America/Manaus=BR|America/Eirunepe=BR|America/Rio_Branco=BR|Asia/Thimphu=BT|Europe/Minsk=BY|America/Belize=BZ|America/St_Johns=CA|America/Halifax=CA|America/Glace_Bay=CA|America/Moncton=CA|America/Goose_Bay=CA|America/Toronto=CA,BS|America/Iqaluit=CA|America/Winnipeg=CA|America/Resolute=CA|America/Rankin_Inlet=CA|America/Regina=CA|America/Swift_Current=CA|America/Edmonton=CA|America/Cambridge_Bay=CA|America/Inuvik=CA|America/Dawson_Creek=CA|America/Fort_Nelson=CA|America/Whitehorse=CA|America/Dawson=CA|America/Vancouver=CA|Europe/Zurich=CH,DE,LI|Africa/Abidjan=CI,BF,GH,GM,GN,IS,ML,MR,SH,SL,SN,TG|Pacific/Rarotonga=CK|America/Santiago=CL|America/Coyhaique=CL|America/Punta_Arenas=CL|Pacific/Easter=CL|Asia/Shanghai=CN|Asia/Urumqi=CN|America/Bogota=CO|America/Costa_Rica=CR|America/Havana=CU|Atlantic/Cape_Verde=CV|Asia/Nicosia=CY|Asia/Famagusta=CY|Europe/Prague=CZ,SK|Europe/Berlin=DE,DK,NO,SE,SJ|America/Santo_Domingo=DO|Africa/Algiers=DZ|America/Guayaquil=EC|Pacific/Galapagos=EC|Europe/Tallinn=EE|Africa/Cairo=EG|Africa/El_Aaiun=EH|Europe/Madrid=ES|Africa/Ceuta=ES|Atlantic/Canary=ES|Europe/Helsinki=FI,AX|Pacific/Fiji=FJ|Atlantic/Stanley=FK|Pacific/Kosrae=FM|Atlantic/Faroe=FO|Europe/Paris=FR,MC|Europe/London=GB,GG,IM,JE|Asia/Tbilisi=GE|America/Cayenne=GF|Europe/Gibraltar=GI|America/Nuuk=GL|America/Danmarkshavn=GL|America/Scoresbysund=GL|America/Thule=GL|Europe/Athens=GR|Atlantic/South_Georgia=GS|America/Guatemala=GT|Pacific/Guam=GU,MP|Africa/Bissau=GW|America/Guyana=GY|Asia/Hong_Kong=HK|America/Tegucigalpa=HN|America/Port-au-Prince=HT|Europe/Budapest=HU|Asia/Jakarta=ID|Asia/Pontianak=ID|Asia/Makassar=ID|Asia/Jayapura=ID|Europe/Dublin=IE|Asia/Jerusalem=IL|Asia/Kolkata=IN|Indian/Chagos=IO|Asia/Baghdad=IQ|Asia/Tehran=IR|Europe/Rome=IT,SM,VA|America/Jamaica=JM|Asia/Amman=JO|Asia/Tokyo=JP,AU|Africa/Nairobi=KE,DJ,ER,ET,KM,MG,SO,TZ,UG,YT|Asia/Bishkek=KG|Pacific/Tarawa=KI,MH,TV,UM,WF|Pacific/Kanton=KI|Pacific/Kiritimati=KI|Asia/Pyongyang=KP|Asia/Seoul=KR|Asia/Almaty=KZ|Asia/Qyzylorda=KZ|Asia/Qostanay=KZ|Asia/Aqtobe=KZ|Asia/Aqtau=KZ|Asia/Atyrau=KZ|Asia/Oral=KZ|Asia/Beirut=LB|Asia/Colombo=LK|Africa/Monrovia=LR|Europe/Vilnius=LT|Europe/Riga=LV|Africa/Tripoli=LY|Africa/Casablanca=MA|Europe/Chisinau=MD|Pacific/Kwajalein=MH|Asia/Yangon=MM,CC|Asia/Ulaanbaatar=MN|Asia/Hovd=MN|Asia/Macau=MO|America/Martinique=MQ|Europe/Malta=MT|Indian/Mauritius=MU|Indian/Maldives=MV,TF|America/Mexico_City=MX|America/Cancun=MX|America/Merida=MX|America/Monterrey=MX|America/Matamoros=MX|America/Chihuahua=MX|America/Ciudad_Juarez=MX|America/Ojinaga=MX|America/Mazatlan=MX|America/Bahia_Banderas=MX|America/Hermosillo=MX|America/Tijuana=MX|Asia/Kuching=MY,BN|Africa/Maputo=MZ,BI,BW,CD,MW,RW,ZM,ZW|Africa/Windhoek=NA|Pacific/Noumea=NC|Pacific/Norfolk=NF|Africa/Lagos=NG,AO,BJ,CD,CF,CG,CM,GA,GQ,NE|America/Managua=NI|Asia/Kathmandu=NP|Pacific/Nauru=NR|Pacific/Niue=NU|Pacific/Auckland=NZ,AQ|Pacific/Chatham=NZ|America/Panama=PA,CA,KY|America/Lima=PE|Pacific/Tahiti=PF|Pacific/Marquesas=PF|Pacific/Gambier=PF|Pacific/Port_Moresby=PG,AQ,FM|Pacific/Bougainville=PG|Asia/Manila=PH|Asia/Karachi=PK|Europe/Warsaw=PL|America/Miquelon=PM|Pacific/Pitcairn=PN|America/Puerto_Rico=PR,AG,CA,AI,AW,BL,BQ,CW,DM,GD,GP,KN,LC,MF,MS,SX,TT,VC,VG,VI|Asia/Gaza=PS|Asia/Hebron=PS|Europe/Lisbon=PT|Atlantic/Madeira=PT|Atlantic/Azores=PT|Pacific/Palau=PW|America/Asuncion=PY|Asia/Qatar=QA,BH|Europe/Bucharest=RO|Europe/Belgrade=RS,BA,HR,ME,MK,SI|Europe/Kaliningrad=RU|Europe/Moscow=RU|Europe/Simferopol=RU,UA|Europe/Kirov=RU|Europe/Volgograd=RU|Europe/Astrakhan=RU|Europe/Saratov=RU|Europe/Ulyanovsk=RU|Europe/Samara=RU|Asia/Yekaterinburg=RU|Asia/Omsk=RU|Asia/Novosibirsk=RU|Asia/Barnaul=RU|Asia/Tomsk=RU|Asia/Novokuznetsk=RU|Asia/Krasnoyarsk=RU|Asia/Irkutsk=RU|Asia/Chita=RU|Asia/Yakutsk=RU|Asia/Khandyga=RU|Asia/Vladivostok=RU|Asia/Ust-Nera=RU|Asia/Magadan=RU|Asia/Sakhalin=RU|Asia/Srednekolymsk=RU|Asia/Kamchatka=RU|Asia/Anadyr=RU|Asia/Riyadh=SA,AQ,KW,YE|Pacific/Guadalcanal=SB,FM|Africa/Khartoum=SD|Asia/Singapore=SG,AQ,MY|America/Paramaribo=SR|Africa/Juba=SS|Africa/Sao_Tome=ST|America/El_Salvador=SV|Asia/Damascus=SY|America/Grand_Turk=TC|Africa/Ndjamena=TD|Asia/Bangkok=TH,CX,KH,LA,VN|Asia/Dushanbe=TJ|Pacific/Fakaofo=TK|Asia/Dili=TL|Asia/Ashgabat=TM|Africa/Tunis=TN|Pacific/Tongatapu=TO|Europe/Istanbul=TR|Asia/Taipei=TW|Europe/Kyiv=UA|America/New_York=US|America/Detroit=US|America/Kentucky/Louisville=US|America/Kentucky/Monticello=US|America/Indiana/Indianapolis=US|America/Indiana/Vincennes=US|America/Indiana/Winamac=US|America/Indiana/Marengo=US|America/Indiana/Petersburg=US|America/Indiana/Vevay=US|America/Chicago=US|America/Indiana/Tell_City=US|America/Indiana/Knox=US|America/Menominee=US|America/North_Dakota/Center=US|America/North_Dakota/New_Salem=US|America/North_Dakota/Beulah=US|America/Denver=US|America/Boise=US|America/Phoenix=US,CA|America/Los_Angeles=US|America/Anchorage=US|America/Juneau=US|America/Sitka=US|America/Metlakatla=US|America/Yakutat=US|America/Nome=US|America/Adak=US|Pacific/Honolulu=US|America/Montevideo=UY|Asia/Samarkand=UZ|Asia/Tashkent=UZ|America/Caracas=VE|Asia/Ho_Chi_Minh=VN|Pacific/Efate=VU|Pacific/Apia=WS|Africa/Johannesburg=ZA,LS,SZ";

    let publicState: PublicState | null = null;
    let participantState: ParticipantState | null = null;
    let accessToken = safeStorageGet(STORAGE_KEY);
    let signupSubmitted = false;
    let selectedCheckInDay: number | null = null;
    const savedDays = new Set<number>();
    const pendingNotePhotos = new Map<string, File[]>();
    const pendingNotePhotoUrls = new Map<string, string[]>();
    const PARTICIPANT_TABS: readonly ParticipantTab[] = ["checkin", "profile", "home"];
    const athleteSelectors = new Map<string, AthleteSelectorController>();
    let athleteDirectory: AthleteOption[] = [];
    let athleteDirectoryPromise: Promise<AthleteOption[]> | null = null;
    let quoteAthleteResults: QuoteAthlete[] = [];
    let boardScrollObserver: ResizeObserver | null = null;
    let boardScrollObservedElement: Element | null = null;
    let dashboardScrollObserver: ResizeObserver | null = null;
    let dashboardScrollObservedElement: Element | null = null;
    let participantActiveTab: ParticipantTab | null = null;
    let participantTabManual = false;
    let participantNotice: ParticipantNotice | null = null;
    let showInactiveLeaderboard = false;
    let commitmentPaymentPollRun = 0;
    let accessTab: AccessTab = "signup";
    let accessLoading = !!accessToken;
    let timeZoneCountryCodes: Map<string, string[]> | null = null;
    let regionDisplayNames: Intl.DisplayNames | null = null;
    let callCountdownTimer: number | null = null;
    let quoteDialogLastFocus: HTMLElement | null = null;

    function readSharedWindowApi(): SharedWindowApi {
        const api: SharedWindowApi = {};
        if ("PhenoAge" in window && isPhenoAgeApi(window.PhenoAge)) api.PhenoAge = window.PhenoAge;
        if ("BortzAge" in window && isBortzAgeApi(window.BortzAge)) api.BortzAge = window.BortzAge;
        if ("calculateAgeAtDate" in window && isAgeAtDateFunction(window.calculateAgeAtDate)) api.calculateAgeAtDate = window.calculateAgeAtDate;
        if ("getGeneration" in window && isGenerationFunction(window.getGeneration)) api.getGeneration = window.getGeneration;
        if ("slugifyName" in window && isSlugifyFunction(window.slugifyName)) api.slugifyName = window.slugifyName;
        return api;
    }

    function isPhenoAgeApi(value: unknown): value is PhenoAgeApi {
        return hasProperties(value, "calculatePhenoAge") && typeof value.calculatePhenoAge === "function";
    }

    function isBortzAgeApi(value: unknown): value is BortzAgeApi {
        return hasProperties(value, "calculateBortzAge") && typeof value.calculateBortzAge === "function";
    }

    function isAgeAtDateFunction(value: unknown): value is (birthDate: Date, atDate: Date) => number {
        return typeof value === "function";
    }

    function isGenerationFunction(value: unknown): value is (birthYear: number) => string {
        return typeof value === "function";
    }

    function isSlugifyFunction(value: unknown): value is (name: string, encode?: boolean) => string {
        return typeof value === "function";
    }

    function requiredElement<T extends HTMLElement>(id: string, expected: abstract new (...args: never[]) => T): T {
        const element = document.getElementById(id);
        if (!(element instanceof expected)) {
            throw new Error(`Required challenge element #${id} is missing or has the wrong type.`);
        }
        return element;
    }

    function optionalElement<T extends HTMLElement>(id: string, expected: abstract new (...args: never[]) => T): T | null {
        const element = document.getElementById(id);
        return element instanceof expected ? element : null;
    }

    function requiredInput(id: string): HTMLInputElement {
        return requiredElement(id, HTMLInputElement);
    }

    function optionalInput(id: string): HTMLInputElement | null {
        return optionalElement(id, HTMLInputElement);
    }

    function requiredSelect(id: string): HTMLSelectElement {
        return requiredElement(id, HTMLSelectElement);
    }

    function optionalSelect(id: string): HTMLSelectElement | null {
        return optionalElement(id, HTMLSelectElement);
    }

    function requiredForm(id: string): HTMLFormElement {
        return requiredElement(id, HTMLFormElement);
    }

    function requiredButton(id: string): HTMLButtonElement {
        return requiredElement(id, HTMLButtonElement);
    }

    function isHTMLElement(value: EventTarget | null): value is HTMLElement {
        return value instanceof HTMLElement;
    }

    function isButton(value: EventTarget | null): value is HTMLButtonElement {
        return value instanceof HTMLButtonElement;
    }

    function isParticipantTab(value: string | undefined): value is ParticipantTab {
        return value === "checkin" || value === "profile" || value === "home";
    }

    document.addEventListener("DOMContentLoaded", init);

    async function init() {
        fillTimeZones(requiredSelect("lmxSignupTimeZone"));
        fillTimeZones(requiredSelect("lmxEditTimeZone"));
        initTimeZonePickers();
        renderQuestionPreview();
        wireForms();
        wireAccessTabs();
        initAthleteSelectors();
        wireIdentityControls();
        startCallCountdownTimer();
        if (accessLoading) renderAccessLoading();

        try {
            await consumeUrlTokens();
            await refreshState();
            scrollBoardToLatestDay();
        } catch (err) {
            setStatus("lmxSignupStatus", messageOf(err), true);
            if (!publicState) await refreshPublicOnly();
        }
    }

    function wireForms() {
        const signupForm = requiredForm("lmxSignupForm");
        const resendForm = requiredForm("lmxResendForm");
        const editForm = requiredForm("lmxEditForm");
        const signupAgain = requiredButton("lmxSignupAgain");
        const signupEmailInput = requiredInput("lmxSignupEmail");
        const resendEmailInput = requiredInput("lmxResendEmail");
        const profilePictureInput = requiredInput("lmxProfilePictureInput");
        const profilePictureButton = requiredButton("lmxProfilePictureButton");
        const editTimeZone = requiredSelect("lmxEditTimeZone");
        const inactiveToggle = optionalElement("lmxInactiveToggle", HTMLButtonElement);
        wireEmailValidityReset(signupEmailInput);
        wireEmailValidityReset(resendEmailInput);
        wireCommitmentAmountValidation("lmxEditCommitmentAmount");

        signupForm.addEventListener("submit", async event => {
            event.preventDefault();
            accessTab = "signup";
            const signupEmail = validateEmailInput(signupEmailInput);
            if (!signupEmail) return;

            await withButton(signupForm.querySelector("button[type='submit']"), async () => {
                const payload = {
                    email: signupEmail,
                    displayName: getIdentityDisplayName("signup"),
                    timeZoneId: requiredSelect("lmxSignupTimeZone").value,
                    athleteLink: getIdentityAthletePayload("signup")
                };
                const result = await postJson(`${API}/signup`, payload);
                setStatus("lmxSignupStatus", result.message || "Check your email.", false);
                signupForm.reset();
                clearAthleteSelector("lmxSignupAthlete");
                setIdentityMode("signup", "participant");
                setDefaultTimezone(requiredSelect("lmxSignupTimeZone"));
                signupSubmitted = true;
                renderAll();
            }, "Joining...");
        });

        signupAgain.addEventListener("click", () => {
            accessTab = "signup";
            signupSubmitted = false;
            setStatus("lmxSignupStatus", "", false);
            renderAll();
        });

        resendForm.addEventListener("submit", async event => {
            event.preventDefault();
            const resendEmail = validateEmailInput(resendEmailInput);
            if (!resendEmail) return;

            await withButton(resendForm.querySelector("button[type='submit']"), async () => {
                await postJson(`${API}/resend`, {
                    email: resendEmail
                });
                setStatus("lmxResendStatus", "Check your email for your private check-in link.", false);
            }, "Sending...");
        });

        document.querySelectorAll<HTMLButtonElement>("[data-lmx-tab]").forEach(button => {
            button.addEventListener("click", () => {
                if (button.disabled || button.getAttribute("aria-disabled") === "true") return;
                setParticipantTab(button.dataset.lmxTab, true);
            });
            button.addEventListener("keydown", event => {
                handleParticipantTabKeydown(event, button);
            });
        });

        profilePictureButton.addEventListener("click", () => {
            profilePictureInput.click();
        });

        profilePictureInput.addEventListener("change", async () => {
            const file = profilePictureInput.files && profilePictureInput.files[0];
            if (file) await uploadProfilePicture(file, profilePictureInput);
        });

        editForm.addEventListener("submit", async event => {
            event.preventDefault();
            if (!accessToken) return;
            await withButton(editForm.querySelector("button[type='submit']"), async () => {
                const result = await postJson(`${API}/edit`, {
                    accessToken,
                    timeZoneId: requiredSelect("lmxEditTimeZone").value,
                    commitmentAmountUsd: parseOptionalCommitmentAmount("lmxEditCommitmentAmount")
                });
                participantState = result;
                publicState = result.public;
                renderAll();
                setStatus("lmxEditStatus", "Saved.", false);
            }, "Saving...");
        });

        inactiveToggle?.addEventListener("click", () => {
            showInactiveLeaderboard = !showInactiveLeaderboard;
            if (publicState) renderBoard(publicState);
        });

        editTimeZone.addEventListener("change", () => {
            if (participantState) renderParticipantCalls(participantState.calls || [], participantState.public.callSelectionClosesAtUtc);
        });
    }

    function wireAccessTabs() {
        document.querySelectorAll<HTMLButtonElement>("[data-lmx-access-tab]").forEach(button => {
            button.addEventListener("click", () => {
                accessTab = button.dataset.lmxAccessTab === "signin" ? "signin" : "signup";
                renderPanels(publicState || {});
            });
            button.addEventListener("keydown", event => {
                handleAccessTabKeydown(event, button);
            });
        });
    }

    function wireIdentityControls() {
        const scopes: readonly IdentityScope[] = ["signup"];
        scopes.forEach(scope => {
            document.querySelectorAll<HTMLInputElement>(`input[name="${identityRadioName(scope)}"]`).forEach(input => {
                input.addEventListener("change", () => updateIdentityScope(scope));
            });
            updateIdentityScope(scope);
        });
    }

    function identityRadioName(scope: IdentityScope): string {
        return scope === "edit" ? "lmxEditIdentity" : "lmxSignupIdentity";
    }

    function identityPrefix(scope: IdentityScope): string {
        return scope === "edit" ? "lmxEdit" : "lmxSignup";
    }

    function getIdentityMode(scope: IdentityScope): IdentityMode {
        return document.querySelector<HTMLInputElement>(`input[name="${identityRadioName(scope)}"]:checked`)?.value === "athlete"
            ? "athlete"
            : "participant";
    }

    function setIdentityMode(scope: IdentityScope, mode: IdentityMode): void {
        const value = mode === "athlete" ? "athlete" : "participant";
        const radio = document.querySelector<HTMLInputElement>(`input[name="${identityRadioName(scope)}"][value="${value}"]`);
        if (radio) radio.checked = true;
        updateIdentityScope(scope);
    }

    function updateIdentityScope(scope: IdentityScope): void {
        const prefix = identityPrefix(scope);
        const athleteMode = getIdentityMode(scope) === "athlete";
        const usernameField = document.getElementById(`${prefix}UsernameField`);
        const athleteField = document.getElementById(`${prefix}AthleteField`);
        const usernameInput = optionalInput(`${prefix}Name`);
        const athleteInput = optionalInput(`${prefix}Athlete`);

        usernameField?.classList.toggle("lmx-hidden", athleteMode);
        athleteField?.classList.toggle("lmx-hidden", !athleteMode);

        if (usernameInput) {
            usernameInput.required = !athleteMode;
            usernameInput.disabled = athleteMode;
            if (athleteMode) usernameInput.setCustomValidity?.("");
        }

        if (athleteInput) {
            athleteInput.required = athleteMode;
            athleteInput.disabled = !athleteMode;
            if (!athleteMode) athleteInput.setCustomValidity?.("");
        }
    }

    function getIdentityDisplayName(scope: IdentityScope): string {
        if (getIdentityMode(scope) !== "athlete")
            return optionalInput(`${identityPrefix(scope)}Name`)?.value.trim() || "";

        const athleteInputId = `${identityPrefix(scope)}Athlete`;
        return getAthleteSelectorDisplayName(athleteInputId);
    }

    function normalizeEmailInput(input: HTMLInputElement | null): string {
        const normalized = normalizeEmailValue(input?.value || "");
        if (input) input.value = normalized;
        return normalized;
    }

    function validateEmailInput(input: HTMLInputElement | null): string | null {
        const normalized = normalizeEmailInput(input);
        if (!input) return normalized;

        input.setCustomValidity("");
        if (!normalized) return normalized;
        if (isEmailAddress(normalized)) return normalized;

        input.setCustomValidity("Enter a valid email address.");
        input.reportValidity?.();
        input.focus();
        return null;
    }

    function wireEmailValidityReset(input: HTMLInputElement | null): void {
        input?.addEventListener("input", () => input.setCustomValidity(""));
    }

    function isEmailAddress(value: string): boolean {
        const input = document.createElement("input");
        input.type = "email";
        input.value = String(value || "");
        return input.checkValidity();
    }

    function normalizeEmailValue(value: string): string {
        let normalized = String(value || "").trim();
        const bracketMatch = /<([^<>]+)>/.exec(normalized);
        if (bracketMatch) {
            normalized = bracketMatch[1]?.trim() || "";
        }

        if (/^mailto:/i.test(normalized)) {
            normalized = normalized.replace(/^mailto:/i, "").split("?")[0]?.trim() || "";
        }

        return normalized;
    }

    function getIdentityAthletePayload(scope: IdentityScope): string | null {
        if (getIdentityMode(scope) !== "athlete")
            return null;

        return getRequiredAthleteSelectorPayload(`${identityPrefix(scope)}Athlete`);
    }

    async function consumeUrlTokens() {
        const params = new URLSearchParams(window.location.search || "");
        let shouldClean = false;

        if (params.has("token")) {
            const token = params.get("token") || "";
            if (token.length > 0) {
                accessToken = token;
                accessLoading = true;
                safeStorageSet(STORAGE_KEY, accessToken);
                shouldClean = true;
                renderAccessLoading();
            }
        }

        if (params.has("confirm")) {
            const result = await postJson(`${API}/confirm`, { token: params.get("confirm") || "" });
            accessToken = result.accessToken;
            safeStorageSet(STORAGE_KEY, accessToken);
            participantState = result.state;
            publicState = result.state.public;
            setStatus("lmxSignupStatus", "You're in.", false);
            shouldClean = true;
        }

        if (params.has("stop")) {
            await postJson(`${API}/stop-emails`, { token: params.get("stop") || "" });
            accessTab = "signin";
            setStatus("lmxResendStatus", "Challenge emails stopped.", false);
            shouldClean = true;
        }

        if (shouldClean) {
            window.history.replaceState({}, "", window.location.pathname);
        }
    }

    async function refreshState() {
        if (participantState && publicState) {
            accessLoading = false;
            renderAll();
            return;
        }

        if (!publicState) {
            await refreshPublicOnly({ keepParticipant: !!accessToken });
        }

        if (accessToken) {
            accessLoading = true;
            renderAccessLoading();
            try {
                participantState = await postJson(`${API}/participant`, { token: accessToken });
                publicState = participantState.public;
                accessLoading = false;
                renderAll();
                return;
            } catch (err) {
                if (isAuthFailure(err)) {
                    safeStorageRemove(STORAGE_KEY);
                    accessToken = null;
                    accessLoading = false;
                    accessTab = "signin";
                } else {
                    setStatus("lmxResendStatus", "Your private console did not load yet. Refresh to try again.", true);
                    accessLoading = false;
                    accessTab = "signin";
                    renderAll();
                    return;
                }
            }
        }

        if (!publicState) {
            await refreshPublicOnly();
        } else {
            participantState = null;
            accessLoading = false;
            renderAll();
        }
    }

    async function refreshPublicOnly(options: RefreshPublicOptions = {}): Promise<void> {
        const keepParticipant = !!(options && options.keepParticipant);
        publicState = await getJson(`${API}/state`);
        if (!keepParticipant) participantState = null;
        renderAll();
    }

    function renderAll() {
        const state = participantState ? participantState.public : publicState;
        if (!state) return;

        renderMetrics(state);
        renderHeroContext(state);
        renderChallengeVisuals(state);
        renderBoard(state);
        renderPanels(state);
        scrollDashboardToLatestDay();

        if (participantState) {
            renderParticipant(participantState);
        } else {
            renderNotes(state.notes || [], false);
        }

        scrollBoardToLatestDay();
    }

    function renderQuestionPreview() {
        const list = document.getElementById("lmxQuestionPreviewList");
        if (!list) return;

        list.innerHTML = QUESTIONS.map(q => `
            <div class="lmx-question-preview-item">
                <div class="lmx-question-preview-label">
                    <i class="fas ${q.icon}" aria-hidden="true"></i>
                    <span>${esc(q.text)}</span>
                </div>
            </div>`).join("");
    }

    function renderMetrics(state: PublicState): void {
        const preStartSignup = isPreStartSignup(state);
        const boardRows = splitLeaderboardRows(state);
        const checks = boardRows.active.reduce((sum, row) => sum + row.checkedInDays, 0);
        const callCount = challengeCallCount(state);
        setText("lmxMetricPeople", String(boardRows.active.length));
        setText("lmxMetricChecks", String(checks));
        setText("lmxMetricMax", String(callCount));
        setText("lmxMetricPhase", phaseLabel(state.phase));
        setText("lmxHeroStatus", phaseLabel(state.phase));
        const boardSection = document.getElementById("lmxBoardSection");
        if (boardSection) boardSection.classList.toggle("signup-roster", preStartSignup);
        if (preStartSignup) {
            setText("lmxBoardTitle", "Leaderboard");
            setText("lmxBoardMeta", `${boardRows.active.length} active people signed up · starts ${formatDateLabel(state.startDate)}`);
        } else {
            setText("lmxBoardTitle", "Live leaderboard");
            setText("lmxBoardMeta", `${boardRows.active.length} active people · ${checks} check-ins · last 2 weeks count · later days score higher · one slip can still score max, never twice in a row`);
        }
    }

    function renderHeroContext(state: PublicState): void {
        const hasParticipant = !!participantState;
        const preStartSignup = isPreStartSignup(state);
        const dashboardMode = hasParticipant || !preStartSignup;
        const titlePanel = document.getElementById("lmxTitlePanel");
        const highlights = document.getElementById("lmxHeroHighlights");
        const life = document.getElementById("lmxLifeStrip");
        if (!highlights || !life) return;
        titlePanel?.classList.toggle("stats-last", !hasParticipant && dashboardMode);

        if (!dashboardMode) {
            toggle("lmxHeroStatus", true);
            toggle("lmxHeroMode", true);
            toggle("lmxHeroCopy", true);
            toggle("lmxLifeStrip", true);
            setText("lmxHeroMode", `Starts ${formatDateLabel(state.startDate)}`);
            setText("lmxHeroCopy", "The first muscle to train is your mind.");
            highlights.className = "lmx-benefit-strip";
            highlights.setAttribute("aria-label", "Challenge benefits");
            highlights.innerHTML = `
                <strong>Fell off your habits?</strong>
                <span>Too busy for a full reset?</span>
                <span>Travel, stress, or deadlines?</span>
                <span>Perfect plans keep failing?</span>`;
            life.className = "lmx-life-strip";
            life.setAttribute("aria-label", "Real life compatible challenge");
            life.innerHTML = `
                <span><i class="fas fa-briefcase" aria-hidden="true"></i>Work compatible</span>
                <span><i class="fas fa-plane" aria-hidden="true"></i>Travel compatible</span>
                <span><i class="fas fa-people-roof" aria-hidden="true"></i>Family compatible</span>
                <span><i class="fas fa-notes-medical" aria-hidden="true"></i>Illness compatible</span>`;
            return;
        }

        if (!hasParticipant) {
            toggle("lmxHeroStatus", false);
            toggle("lmxHeroMode", false);
            toggle("lmxHeroCopy", true);
            toggle("lmxLifeStrip", true);
            setText("lmxHeroCopy", "The first muscle to train is your mind.");
            highlights.className = "lmx-benefit-strip lmx-ops-strip";
            highlights.setAttribute("aria-label", "Challenge status");
            const boardRows = splitLeaderboardRows(state);
            const callCount = challengeCallCount(state);
            highlights.innerHTML = [
                opsTile("People", boardRows.active.length, "fa-users"),
                opsTile("Check-ins", boardRows.active.reduce((sum, row) => sum + row.checkedInDays, 0), "fa-list-check"),
                opsTile(responsiveLabel("Calls", "Community calls"), callCount, "fa-layer-group", "community-calls"),
                opsTile("", phaseLabel(state.phase), "fa-signal")
            ].join("");
            life.className = "lmx-life-strip lmx-ops-status";
            life.setAttribute("aria-label", "Challenge compatibility");
            life.innerHTML = `
                <span><i class="fas fa-briefcase" aria-hidden="true"></i>Work compatible</span>
                <span><i class="fas fa-plane" aria-hidden="true"></i>Travel compatible</span>
                <span><i class="fas fa-people-roof" aria-hidden="true"></i>Family compatible</span>
                <span><i class="fas fa-notes-medical" aria-hidden="true"></i>Illness compatible</span>`;
            return;
        }

        const activeParticipantState = participantState;
        if (!activeParticipantState) return;
        const participant = activeParticipantState.participant;
        const leaderboardRows = splitLeaderboardRows(state);
        const leaderboard = participant.challengeInactive ? (state.leaderboard || []) : leaderboardRows.active;
        const rowIndex = leaderboard.findIndex(row => row.participantId === participant.id);
        const row = rowIndex >= 0 ? leaderboard[rowIndex] : null;
        const daysIn = Math.max(0, Math.trunc(Number(participant.daysIn) || 0));

        toggle("lmxHeroStatus", true);
        toggle("lmxHeroMode", true);
        toggle("lmxHeroCopy", true);
        toggle("lmxLifeStrip", false);
        setText("lmxHeroMode", "You're in");
        setText("lmxHeroCopy", "The first muscle to train is your mind.");
        highlights.className = "lmx-benefit-strip lmx-ops-strip";
        highlights.setAttribute("aria-label", "Participant status");
        highlights.innerHTML = [
            opsTile("Rank", row ? `#${rowIndex + 1}` : "-", "fa-ranking-star"),
            opsTile("Days in", daysIn, "fa-calendar-check"),
            opsTile("Score", row ? row.totalPoints : 0, "fa-bolt"),
            opsTile("Streak", row ? row.currentStreak : 0, "fa-fire")
        ].join("");
    }

    function responsiveLabel(shortLabel: string, longLabel: string): ResponsiveLabel {
        return { shortLabel, longLabel };
    }

    function opsTile(label: string | ResponsiveLabel, value: string | number, icon: string, modifier = ""): string {
        const hasLabel = !!String(label || "").trim();
        const labelHtml = opsTileLabelHtml(label);
        return `<div class="lmx-ops-tile${hasLabel ? "" : " no-label"}${modifier ? ` ${escAttr(modifier)}` : ""}">
            <i class="fas ${escAttr(icon)}" aria-hidden="true"></i>
            ${labelHtml}
            <strong>${esc(value)}</strong>
        </div>`;
    }

    function opsTileLabelHtml(label: string | ResponsiveLabel): string {
        if (label && typeof label === "object") {
            return `<span class="lmx-ops-label">
                <span class="lmx-ops-label-short">${esc(label.shortLabel || "")}</span>
                <span class="lmx-ops-label-long">${esc(label.longLabel || label.shortLabel || "")}</span>
            </span>`;
        }
        return String(label || "").trim() ? `<span class="lmx-ops-label">${esc(label)}</span>` : "";
    }

    function challengeCallCount(state: PublicState): number {
        const dayCount = (state?.days || []).length;
        if (dayCount > 0) return Math.max(1, Math.ceil(dayCount / 7));

        const start = parseIsoDate(state?.startDate);
        if (!start) return 1;

        const elapsedDays = Math.floor((Date.now() - start.getTime()) / 86400000) + 1;
        return Math.max(1, Math.ceil(elapsedDays / 7));
    }

    function renderChallengeVisuals(state: PublicState): void {
        const track = document.getElementById("lmxTrack");
        if (!track) return;

        if (!participantState) {
            track.innerHTML = "";
            return;
        }

        const participant = participantState.participant || {};
        const row = (state.leaderboard || []).find(item => item.participantId === participant.id);
        const cells = normalizeDashboardCells(row, state);
        const visibleDays = cells.length || state.durationDays || 14;
        const dayCount = Math.max(1, Math.trunc(Number(visibleDays) || 14));
        const scoringWindowDays = leaderboardScoringWindowDays(state);
        const scoringWindowCells = cells.slice(Math.max(0, cells.length - scoringWindowDays));
        const scoredCells = scoringWindowCells.filter(cell => cell.checkedIn && cell.countsForScore !== false);
        const checkedCells = scoringWindowCells.filter(cell => cell.checkedIn);
        const categories = dashboardCategories();
        const summaries = categories.map(category => categorySummary(category, scoringWindowCells, scoredCells));
        const rankedSummaries = summaries
            .filter(item => item.max > 0)
            .sort((a, b) => b.rate - a.rate || b.total - a.total || a.category.label.localeCompare(b.category.label));
        const best = rankedSummaries[0];
        const focus = [...rankedSummaries].reverse()[0];
        const fullDays = checkedCells.filter(cell => isLockedInDay(cell, categories)).length;
        const totalPoints = row && typeof row.totalPoints === "number"
            ? row.totalPoints
            : scoredCells.reduce((sum, cell) => sum + (typeof cell.score === "number" ? cell.score : 0), 0);
        const today = new Date().toISOString().slice(0, 10);
        const dayHeaders = cells.map(cell => {
            const classes = ["lmx-dashboard-day"];
            if (cell.date === today) classes.push("today");
            if (cell.countsForScore === false) classes.push("practice");
            return `<span class="${classes.join(" ")}" title="${escAttr(dayTitle(cell))}">${cell.challengeDay}</span>`;
        }).join("");
        const rows = summaries.map(summary => categoryDashboardRow(summary, cells, today)).join("");
        const emptyLabel = state.phase === "signup" || state.phase === "roster" ? "Starts soon" : "No check-ins";

        track.innerHTML = `
            <div class="lmx-dashboard-head">
                <div>
                    <span class="lmx-mini-label">your trend</span>
                </div>
                <strong>${checkedCells.length ? `${checkedCells.length}/${scoringWindowDays} days` : emptyLabel}</strong>
            </div>
            <div class="lmx-dashboard-stats" aria-label="Personal challenge stats">
                ${dashboardStat("Best", best ? best.category.label : "-", best ? `${Math.round(best.rate * 100)}%` : "-", best ? best.category.icon : "fa-arrow-trend-up", best ? best.category.tone : "")}
                ${dashboardStat("Focus", focus ? focus.category.label : "-", focus ? `${Math.round(focus.rate * 100)}%` : "-", focus ? focus.category.icon : "fa-crosshairs", focus ? focus.category.tone : "")}
                ${dashboardStat("Locked-in days", String(fullDays), "", "fa-calendar-check")}
                ${dashboardStat("Points", scoredCells.length ? String(totalPoints) : "-", "", "fa-chart-line")}
            </div>
            ${participantState.trendGuidance?.text ? `<div class="lmx-trend-guidance"><i class="fas fa-scale-balanced" aria-hidden="true"></i><span>${esc(participantState.trendGuidance.text)}</span></div>` : ""}
            <div class="lmx-dashboard-scroll">
                <div class="lmx-dashboard-grid" role="table" aria-label="Sleep, exercise, nutrition, and vices over time" style="--lmx-dashboard-day-columns: repeat(${dayCount}, 2.15rem); --lmx-dashboard-min-width: ${(13.05 + (dayCount * 2.5)).toFixed(2)}rem;">
                    <div class="lmx-dashboard-row lmx-dashboard-row-head" role="row">
                        <div class="lmx-dashboard-corner" role="columnheader">Agency</div>
                        <div class="lmx-dashboard-days" role="presentation">${dayHeaders}</div>
                    </div>
                    ${rows}
                </div>
            </div>`;
    }

    function normalizeDashboardCells(row: LeaderboardRow | null | undefined, state: PublicState): DashboardCell[] {
        const byDay = new Map(((row && row.cells) || []).map(cell => [cell.challengeDay, cell]));
        return (state.days || []).map(day => {
            const cell = byDay.get(day.challengeDay) || {
                challengeDay: day.challengeDay,
                checkedIn: false,
                score: null,
                countsForScore: day.challengeDay !== 1,
                sleep: null,
                exercise: null,
                nutrition: null,
                vices: null
            };
            return { ...cell, date: day.date };
        });
    }

    function dashboardCategories(): DashboardCategory[] {
        return [
            { key: "sleep", label: "Sleep", icon: "fa-moon", tone: "sleep" },
            { key: "exercise", label: "Exercise", icon: "fa-dumbbell", tone: "exercise" },
            { key: "nutrition", label: "Nutrition", icon: "fa-bowl-food", tone: "nutrition" },
            { key: "vices", label: "Vices", icon: "fa-shield-halved", tone: "vices" }
        ];
    }

    function categorySummary(category: DashboardCategory, cells: DashboardCell[], scoredCells: DashboardCell[]): CategorySummary {
        const denominatorCells = scoredCells.length ? scoredCells : cells.filter(cell => cell.checkedIn);
        const total = denominatorCells.reduce((sum, cell) => sum + clampHabitValue(cell[category.key]), 0);
        const max = denominatorCells.length * 2;
        return {
            category,
            total,
            max,
            rate: max > 0 ? total / max : 0
        };
    }

    function categoryDashboardRow(summary: CategorySummary, cells: DashboardCell[], today: string): string {
        const category = summary.category;
        const width = summary.max > 0 ? Math.round(summary.rate * 100) : 0;
        const dayCells = cells.map(cell => categoryDayCell(category, cell, today)).join("");
        return `<div class="lmx-dashboard-row" role="row">
            <div class="lmx-dashboard-category ${escAttr(category.tone)}" role="cell">
                <i class="fas ${escAttr(category.icon)}" aria-hidden="true"></i>
                <span>${esc(category.label)}</span>
                <strong>${summary.max > 0 ? `${summary.total}/${summary.max}` : "-"}</strong>
                <div class="lmx-dashboard-bar" aria-hidden="true"><span style="width:${width}%"></span></div>
            </div>
            <div class="lmx-dashboard-days" role="cell" aria-label="${escAttr(`${category.label} by challenge day`)}">${dayCells}</div>
        </div>`;
    }

    function categoryDayCell(category: DashboardCategory, cell: DashboardCell, today: string): string {
        const classes = ["lmx-category-day"];
        if (cell.date === today) classes.push("today");
        if (cell.countsForScore === false) classes.push("practice");
        if (!cell.checkedIn) {
            classes.push("empty");
            return `<span class="${classes.join(" ")}" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(`${dayTitle(cell)}: no check-in`)}" aria-label="${escAttr(`${category.label} day ${cell.challengeDay}: no check-in`)}"></span>`;
        }

        const value = clampHabitValue(cell[category.key]);
        classes.push(value >= 2 ? "full" : value > 0 ? "partial" : "missed");
        return `<span class="${classes.join(" ")}" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(`${dayTitle(cell)}: ${category.label} ${value}/2`)}" aria-label="${escAttr(`${category.label} day ${cell.challengeDay}: ${value} of 2`)}"></span>`;
    }

    function dashboardStat(label: string, value: string, detail: string, icon: string, tone = ""): string {
        const toneClass = tone ? ` ${escAttr(tone)}` : "";
        return `<div class="lmx-dashboard-stat${toneClass}">
            <i class="fas ${escAttr(icon)}" aria-hidden="true"></i>
            <span>${esc(label)}</span>
            <strong>${esc(value)}</strong>
            ${detail ? `<em>${esc(detail)}</em>` : ""}
        </div>`;
    }

    function isLockedInDay(cell: DashboardCell, categories: DashboardCategory[]): boolean {
        return categories.every(category => clampHabitValue(cell[category.key]) >= 2);
    }

    function clampHabitValue(value: number | null): number {
        const number = Number(value);
        return Number.isFinite(number) ? Math.max(0, Math.min(2, number)) : 0;
    }

    function dayTitle(cell: DashboardCell): string {
        const date = cell.date ? ` · ${formatCheckInDate(cell.date)}` : "";
        const practice = cell.countsForScore === false ? " · practice" : "";
        return `Day ${cell.challengeDay}${date}${practice}`;
    }

    function renderPanels(state: Partial<PublicState>): void {
        const currentParticipantState = participantState;
        const hasParticipant = currentParticipantState !== null;
        const isAccessLoading = accessLoading && !hasParticipant;
        const pendingCheckInDays = currentParticipantState ? getPendingCheckInDays(currentParticipantState) : [];
        const commitmentBlocked = hasCommitmentBlock(currentParticipantState);
        const activeParticipantTab = currentParticipantState ? ensureParticipantTab(currentParticipantState) : null;
        const checkInOnly = !commitmentBlocked && pendingCheckInDays.length > 0 && activeParticipantTab === "checkin";
        const participantGateOnly = commitmentBlocked || checkInOnly;
        const publicContentHidden = checkInOnly;
        const dashboardMode = hasParticipant || !isPreStartSignup(state);
        const hero = document.getElementById("lmxHeroLayout");
        if (hero) {
            hero.classList.toggle("checkin-only", publicContentHidden);
        }

        toggle("lmxTitlePanel", !publicContentHidden);
        toggle("lmxAccessTabs", !hasParticipant && !isAccessLoading);
        toggle("lmxSignupPanel", !hasParticipant && !isAccessLoading && accessTab === "signup");
        toggle("lmxAccessLoadingPanel", isAccessLoading);
        toggle("lmxParticipantPanel", hasParticipant);
        toggle("lmxResendPanel", !hasParticipant && !isAccessLoading && accessTab === "signin");
        toggle("lmxNotesPanel", dashboardMode && !publicContentHidden);
        toggle("lmxSignupIntro", !signupSubmitted);
        toggle("lmxSignupDonePanel", signupSubmitted);
        toggle("lmxHabitHeading", !hasParticipant);
        toggle("lmxHabitGrid", !hasParticipant);
        toggle("lmxQuestionPreview", !commitmentBlocked && (!hasParticipant || pendingCheckInDays.length > 0));
        toggle("lmxTrack", hasParticipant && dashboardMode && !participantGateOnly);
        toggle("lmxMetrics", hasParticipant && dashboardMode && !participantGateOnly);
        toggle("lmxBoardSection", !publicContentHidden);
        toggle("lmxParticipantTabs", hasParticipant && !commitmentBlocked);
        toggle("lmxCommitmentPanel", currentParticipantState !== null && activeParticipantTab !== null && shouldShowCommitmentPanel(currentParticipantState, activeParticipantTab));
        toggle("lmxCheckinPanel", hasParticipant && !commitmentBlocked && activeParticipantTab === "checkin");
        toggle("lmxEditForm", hasParticipant && !commitmentBlocked && activeParticipantTab === "profile");
        toggle("lmxHomePanel", hasParticipant && !commitmentBlocked && activeParticipantTab === "home");
        toggle("lmxParticipantTools", hasParticipant && !commitmentBlocked && activeParticipantTab === "home");
        toggle("lmxParticipantCalls", hasParticipant && !commitmentBlocked && activeParticipantTab === "home");
        renderParticipantTabs();
        if (!hasParticipant) {
            participantActiveTab = null;
            participantTabManual = false;
            participantNotice = null;
            setText("lmxResendButtonText", "Send check-in link");
        }
        renderAccessTabs();
        const slackInvite = optionalElement("lmxSlackInviteLink", HTMLAnchorElement);
        if (slackInvite) {
            slackInvite.href = state.slackInviteUrl || "#";
            slackInvite.classList.toggle("lmx-hidden", !state.slackInviteUrl);
        }

        const slackRoom = optionalElement("lmxSlackRoomLink", HTMLAnchorElement);
        if (slackRoom) {
            slackRoom.href = state.slackRoomUrl || "#";
            slackRoom.classList.toggle("lmx-hidden", !state.slackRoomUrl);
        }
    }

    function renderAccessLoading() {
        toggle("lmxAccessTabs", false);
        toggle("lmxSignupPanel", false);
        toggle("lmxResendPanel", false);
        toggle("lmxParticipantPanel", false);
        toggle("lmxAccessLoadingPanel", true);
    }

    function renderParticipant(state: ParticipantState): void {
        const participant = state.participant;
        const pendingCheckInDays = getPendingCheckInDays(state);
        const activeTab = ensureParticipantTab(state);
        const title = participantPanelTitle(activeTab, pendingCheckInDays, participant, state.public.phase);
        const kicker = participantPanelKicker(activeTab, pendingCheckInDays, state.public.phase);
        setText("lmxParticipantKicker", kicker);
        toggle("lmxParticipantKicker", !!kicker);
        setText("lmxParticipantTitle", title);
        renderCommitmentPanel(state, activeTab);
        renderParticipantNotice();

        renderProfileIdentity(participant);
        setSelectValue(requiredSelect("lmxEditTimeZone"), participant.timeZoneId);
        setCommitmentInputValue("lmxEditCommitmentAmount", participant.commitmentAmountUsd ?? state.commitment?.amountUsd);
        toggle("lmxEditCommitmentField", shouldShowCommitmentAmountField(state));
        const commitmentInput = optionalInput("lmxEditCommitmentAmount");
        if (commitmentInput) {
            const showCommitment = shouldShowCommitmentAmountField(state);
            commitmentInput.disabled = !showCommitment || state.commitment?.canEditAmount === false;
            commitmentInput.required = hasConfiguredCommitmentAmount(state);
        }
        renderProfilePictureControls(participant);
        renderParticipantCalls(state.calls || [], state.public.callSelectionClosesAtUtc);
        if (!hasCommitmentBlock(state)) renderCheckIns(state.eligibleDays || [], undefined, recentPublicRemarks(state));
        renderNotes(state.notes || state.public.notes || [], true);
        renderParticipantTabs();
    }

    function participantPanelTitle(activeTab: ParticipantTab, pendingCheckInDays: EligibleDay[], participant: ParticipantSummary, phase: string): string {
        const name = participant.displayName || "participant";
        const commitment = participantState?.commitment;
        if (commitment?.blocksParticipant) {
            return commitment.status === "due"
                ? `Commitment due, ${name}`
                : "Make a pledge";
        }
        if (activeTab === "profile") return `Profile, ${name}`;
        if (activeTab === "home") {
            return "Home";
        }

        return pendingCheckInDays.length ? `Check in, ${name}` : `Check-in, ${name}`;
    }

    function participantPanelKicker(activeTab: ParticipantTab, pendingCheckInDays: EligibleDay[], phase: string): string {
        if (hasCommitmentBlock(participantState)) return "pledge";
        if (activeTab === "profile") return "profile";
        if (activeTab === "home") {
            return "";
        }

        return pendingCheckInDays.length ? "due now" : "no due day";
    }

    function renderParticipantNotice() {
        const notice = document.getElementById("lmxParticipantNotice");
        if (!notice) return;

        const currentNotice = participantNotice;
        const visible = !!(currentNotice?.message && participantState && !hasCommitmentBlock(participantState));
        notice.textContent = visible ? currentNotice?.message ?? "" : "";
        notice.classList.toggle("lmx-hidden", !visible);
        notice.classList.toggle("error", visible && !!currentNotice?.isError);
        notice.classList.toggle("success", visible && !currentNotice?.isError);
    }

    function ensureParticipantTab(state: ParticipantState): ParticipantTab {
        const fallback = getDefaultParticipantTab(state);
        if (!participantActiveTab || !PARTICIPANT_TABS.includes(participantActiveTab)) {
            participantActiveTab = fallback;
            participantTabManual = false;
            return participantActiveTab;
        }

        if (isParticipantTabLocked(participantActiveTab, state)) {
            participantActiveTab = fallback;
            participantTabManual = false;
            return participantActiveTab;
        }

        if (!participantTabManual && participantActiveTab !== fallback) {
            participantActiveTab = fallback;
        }

        return participantActiveTab;
    }

    function getDefaultParticipantTab(state: ParticipantState): ParticipantTab {
        return getPendingCheckInDays(state).length ? "checkin" : "home";
    }

    function renderAccessTabs() {
        [
            { tab: "signup", buttonId: "lmxSignupTab", panelId: "lmxSignupPanel" },
            { tab: "signin", buttonId: "lmxSigninTab", panelId: "lmxResendPanel" }
        ].forEach(item => {
            const isActive = accessTab === item.tab;
            const button = document.getElementById(item.buttonId);
            const panel = document.getElementById(item.panelId);
            if (button) {
                button.setAttribute("aria-selected", isActive ? "true" : "false");
                button.setAttribute("tabindex", isActive ? "0" : "-1");
            }
            if (panel) {
                const hidden = panel.classList.contains("lmx-hidden") || !isActive;
                panel.toggleAttribute("hidden", hidden);
            }
        });
    }

    function handleAccessTabKeydown(event: KeyboardEvent, button: HTMLButtonElement): void {
        const tabs: readonly AccessTab[] = ["signup", "signin"];
        const requestedTab = button.dataset.lmxAccessTab;
        if (requestedTab !== "signup" && requestedTab !== "signin") return;
        const currentIndex = tabs.indexOf(requestedTab);
        if (currentIndex < 0) return;

        let nextIndex = currentIndex;
        if (event.key === "ArrowRight" || event.key === "ArrowDown") {
            nextIndex = (currentIndex + 1) % tabs.length;
        } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
            nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
        } else if (event.key === "Home") {
            nextIndex = 0;
        } else if (event.key === "End") {
            nextIndex = tabs.length - 1;
        } else {
            return;
        }

        event.preventDefault();
        const nextTab = tabs[nextIndex];
        if (!nextTab) return;
        accessTab = nextTab;
        renderPanels(publicState || {});
        document.querySelector<HTMLElement>(`[data-lmx-access-tab="${accessTab}"]`)?.focus();
    }

    function setParticipantTab(tab: string | undefined, manual: boolean): void {
        if (!isParticipantTab(tab) || !participantState) return;
        if (isParticipantTabLocked(tab, participantState)) return;
        participantActiveTab = tab;
        participantTabManual = !!manual;
        renderPanels(participantState.public);
        renderParticipant(participantState);
        if (tab === "checkin") {
            document.getElementById("lmxParticipantPanel")?.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
    }

    function renderParticipantTabs() {
        const currentParticipantState = participantState;
        if (!currentParticipantState) return;
        if (hasCommitmentBlock(currentParticipantState)) {
            PARTICIPANT_TABS.forEach(tab => {
                const panel = getParticipantTabPanel(tab);
                const button = document.querySelector<HTMLButtonElement>(`[data-lmx-tab="${tab}"]`);
                if (button) {
                    button.setAttribute("aria-selected", "false");
                    button.setAttribute("tabindex", "-1");
                    button.hidden = true;
                }
                if (panel) {
                    panel.classList.add("lmx-hidden");
                    panel.toggleAttribute("hidden", true);
                }
            });
            return;
        }

        const activeTab = ensureParticipantTab(currentParticipantState);
        PARTICIPANT_TABS.forEach(tab => {
            const button = document.querySelector<HTMLButtonElement>(`[data-lmx-tab="${tab}"]`);
            const panel = getParticipantTabPanel(tab);
            const isActive = tab === activeTab;
            const locked = isParticipantTabLocked(tab, currentParticipantState);
            if (button) {
                button.setAttribute("aria-selected", isActive ? "true" : "false");
                button.setAttribute("tabindex", locked ? "-1" : (isActive ? "0" : "-1"));
                button.toggleAttribute("disabled", locked);
                button.setAttribute("aria-disabled", locked ? "true" : "false");
                button.title = locked ? "Save today's check-in first." : "";
                button.hidden = false;
            }
            if (panel) {
                panel.classList.toggle("lmx-hidden", !isActive);
                panel.toggleAttribute("hidden", !isActive);
            }
        });
    }

    function getParticipantTabPanel(tab: ParticipantTab): HTMLElement | null {
        if (tab === "checkin") return document.getElementById("lmxCheckinPanel");
        if (tab === "profile") return document.getElementById("lmxEditForm");
        if (tab === "home") return document.getElementById("lmxHomePanel");
        return null;
    }

    function handleParticipantTabKeydown(event: KeyboardEvent, button: HTMLButtonElement): void {
        const currentParticipantState = participantState;
        if (!currentParticipantState || hasCommitmentBlock(currentParticipantState)) return;

        const availableTabs = PARTICIPANT_TABS.filter(tab => !isParticipantTabLocked(tab, currentParticipantState));
        const requestedTab = button.dataset.lmxTab;
        if (!isParticipantTab(requestedTab)) return;
        const currentIndex = availableTabs.indexOf(requestedTab);
        if (currentIndex < 0) return;

        let nextIndex = currentIndex;
        if (event.key === "ArrowRight" || event.key === "ArrowDown") {
            nextIndex = (currentIndex + 1) % availableTabs.length;
        } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
            nextIndex = (currentIndex - 1 + availableTabs.length) % availableTabs.length;
        } else if (event.key === "Home") {
            nextIndex = 0;
        } else if (event.key === "End") {
            nextIndex = availableTabs.length - 1;
        } else {
            return;
        }

        event.preventDefault();
        const nextTab = availableTabs[nextIndex];
        if (!nextTab) return;
        setParticipantTab(nextTab, true);
        document.querySelector<HTMLElement>(`[data-lmx-tab="${nextTab}"]`)?.focus();
    }

    function isParticipantTabLocked(tab: ParticipantTab, state: ParticipantState): boolean {
        return tab !== "checkin" && getPendingCheckInDays(state).length > 0;
    }

    function renderCommitmentPanel(state: ParticipantState, activeTab: ParticipantTab): void {
        const panel = document.getElementById("lmxCommitmentPanel");
        if (!panel) return;

        const commitment = state.commitment || {};
        if (shouldShowCheckInPledgePrompt(state, activeTab)) {
            panel.innerHTML = `
                <form id="lmxCommitmentAmountForm" class="lmx-commitment-card setup" data-commitment-prompt="optional">
                    <div class="lmx-commitment-main">
                        <i class="fas fa-pen-nib" aria-hidden="true"></i>
                        <div>
                            <strong>Set a real stake</strong>
                            <span id="lmxPledgeCommitmentHelp">Fall below your recent average and either pay it or stop longevitymaxxing. You can keep checking in without a pledge.</span>
                        </div>
                    </div>
                    <div class="lmx-field">
                        <label for="lmxPledgeCommitmentAmount">Pledge</label>
                        <div class="lmx-money-input">
                            <span aria-hidden="true">$</span>
                            <input id="lmxPledgeCommitmentAmount" type="text" inputmode="decimal" required placeholder="300" aria-describedby="lmxPledgeCommitmentHelp">
                        </div>
                    </div>
                    <button class="lmx-button secondary" type="submit">
                        <i class="fas fa-pen-nib" aria-hidden="true"></i>
                        Make a pledge
                    </button>
                    <div class="lmx-status" role="status" aria-live="polite" aria-atomic="true"></div>
                </form>`;
            panel.querySelector("form")?.addEventListener("submit", event => {
                event.preventDefault();
                saveCommitmentAmountFromPanel("lmxPledgeCommitmentAmount", panel.querySelector<HTMLButtonElement>("button[type='submit']"), "Pledge saved.");
            });
            wireCommitmentAmountValidation("lmxPledgeCommitmentAmount");
            return;
        }

        if (!commitment.blocksParticipant) {
            panel.innerHTML = "";
            return;
        }

        if (commitment.status === "needs-amount") {
            panel.innerHTML = `
                <form id="lmxCommitmentAmountForm" class="lmx-commitment-card setup" data-commitment-block="true">
                    <div class="lmx-commitment-main">
                        <i class="fas fa-lock" aria-hidden="true"></i>
                        <div>
                            <strong>Set a real stake</strong>
                            <span id="lmxBlockedCommitmentHelp">Fall below your recent average and either pay it or stop. Choose an amount that would hurt.</span>
                        </div>
                    </div>
                    <div class="lmx-field">
                        <label for="lmxBlockedCommitmentAmount">Pledge</label>
                        <div class="lmx-money-input">
                            <span aria-hidden="true">$</span>
                            <input id="lmxBlockedCommitmentAmount" type="text" inputmode="decimal" required placeholder="300" aria-describedby="lmxBlockedCommitmentHelp">
                        </div>
                    </div>
                    <button class="lmx-button" type="submit">
                        <i class="fas fa-pen-nib" aria-hidden="true"></i>
                        Make a pledge
                    </button>
                    <div class="lmx-status" role="status" aria-live="polite" aria-atomic="true"></div>
                </form>`;
            panel.querySelector("form")?.addEventListener("submit", event => {
                event.preventDefault();
                saveCommitmentAmountFromPanel("lmxBlockedCommitmentAmount", panel.querySelector<HTMLButtonElement>("button[type='submit']"), "Commitment amount saved. You can continue.");
            });
            wireCommitmentAmountValidation("lmxBlockedCommitmentAmount");
            return;
        }

        const invoiceStatus = String(commitment.invoiceStatus || "");
        const hasInvoice = !!(commitment.invoiceId || commitment.checkoutLink || invoiceStatus);
        const replacesInvoice = ["expired", "failed", "invalid"].includes(invoiceStatus.toLowerCase());
        const payText = "Redeem yourself";
        const payBusyText = hasInvoice && !replacesInvoice ? "Opening..." : "Preparing...";
        const showCheckAgain = hasInvoice && !replacesInvoice;
        const editableDays = getCommitmentEditableDays(state);
        panel.innerHTML = `
            <div class="lmx-commitment-card due" data-commitment-block="true">
                <div class="lmx-commitment-main">
                    <i class="fas fa-triangle-exclamation" aria-hidden="true"></i>
                    <div>
                        <strong>Commitment due</strong>
                        <span>${esc(commitment.message || "This check-in landed below your recent average. Pay the locked amount, or improve the editable check-in enough to clear it.")}</span>
                    </div>
                    <b aria-label="${escAttr(`Commitment due amount ${formatUsd(commitment.owedAmountUsd)}`)}">${esc(formatUsd(commitment.owedAmountUsd))}</b>
                </div>
                <div class="lmx-commitment-meta">
                    <span>Trigger: Day ${esc(commitment.triggerChallengeDay || "-")}</span>
                    <span>Score: ${esc(commitment.triggerScore ?? "-")}</span>
                    <span>Baseline: ${esc(formatNumber(commitment.thresholdAverage))}</span>
                </div>
                <div class="lmx-button-row">
                    <button id="lmxCommitmentPayButton" class="lmx-button" type="button" data-busy-text="${escAttr(payBusyText)}">
                        <i class="fas fa-credit-card" aria-hidden="true"></i>
                        ${esc(payText)}
                    </button>
                    <button id="lmxCommitmentCheckButton" class="lmx-button secondary${showCheckAgain ? "" : " lmx-hidden"}" type="button">
                        <i class="fas fa-rotate" aria-hidden="true"></i>
                        Check again
                    </button>
                </div>
                <div id="lmxCommitmentStatus" class="lmx-status" role="status" aria-live="polite" aria-atomic="true"></div>
            </div>
            <div class="lmx-commitment-edit">
                <strong>Eligible fixes</strong>
                <div id="lmxCommitmentCheckinList" class="lmx-checkin-list"></div>
            </div>`;

        panel.querySelector("#lmxCommitmentPayButton")?.addEventListener("click", event => {
            payCommitment(isButton(event.currentTarget) ? event.currentTarget : null);
        });
        panel.querySelector("#lmxCommitmentCheckButton")?.addEventListener("click", event => {
            checkCommitmentPayment(isButton(event.currentTarget) ? event.currentTarget : null, { showWaiting: true, finalWaitingMessage: "Still waiting. This can take a minute." });
        });
        if (editableDays.length) {
            renderCheckIns(editableDays, "lmxCommitmentCheckinList", recentPublicRemarks(state));
        } else {
            const list = document.getElementById("lmxCommitmentCheckinList");
            if (list) {
                list.innerHTML = `<div class="lmx-empty-state">
                    <i class="fas fa-lock" aria-hidden="true"></i>
                    <strong>No editable fix available.</strong>
                    <span>The edit window closed, so payment is required to continue.</span>
                </div>`;
            }
        }
    }

    function hasCommitmentBlock(state: ParticipantState | null): boolean {
        return !!(state && state.commitment && state.commitment.blocksParticipant);
    }

    function shouldShowCommitmentPanel(state: ParticipantState, activeTab: ParticipantTab): boolean {
        return !!(state && state.commitment && (state.commitment.blocksParticipant || shouldShowCheckInPledgePrompt(state, activeTab)));
    }

    function shouldShowCheckInPledgePrompt(state: ParticipantState, activeTab: ParticipantTab): boolean {
        if (activeTab !== "checkin") return false;
        if (state?.commitment?.status !== "deferred") return false;
        const pendingScoredDays = getPendingCheckInDays(state).filter(day => day.countsForScore !== false);
        if (!pendingScoredDays.length) return false;
        return Number(state.trendGuidance?.priorScoredDays || 0) >= 3;
    }

    function shouldShowCommitmentAmountField(state: ParticipantState): boolean {
        return !!(state && state.commitment);
    }

    function hasConfiguredCommitmentAmount(state: ParticipantState): boolean {
        return Number(state?.participant?.commitmentAmountUsd ?? state?.commitment?.amountUsd ?? 0) >= 1;
    }

    function getCommitmentEditableDays(state: ParticipantState): EligibleDay[] {
        const triggerDay = Number(state?.commitment?.triggerChallengeDay || 0);
        return ((state && state.eligibleDays) || [])
            .filter(day => day.existing && (!triggerDay || day.challengeDay === triggerDay));
    }

    async function saveCommitmentAmountFromPanel(inputId: string, button: HTMLButtonElement | null, successMessage: string): Promise<void> {
        if (!accessToken || !participantState) return;
        const currentParticipantState = participantState;
        const currentAccessToken = accessToken;
        await withButton(button, async () => {
            const participant = currentParticipantState.participant;
            const result = await postJson(`${API}/edit`, {
                accessToken: currentAccessToken,
                displayName: participant.displayName || "",
                timeZoneId: participant.timeZoneId || Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
                athleteLink: participant.athleteSlug || participant.athleteUrl || null,
                commitmentAmountUsd: parseCommitmentAmount(inputId)
            });
            participantState = result;
            publicState = result.public;
            if (!hasCommitmentBlock(result)) {
                participantNotice = { message: successMessage, isError: false };
                participantActiveTab = null;
                participantTabManual = false;
            }
            renderAll();
        }, "Saving...");
    }

    function normalizeCheckoutLink(value: unknown): string {
        const raw = typeof value === "string" ? value.trim() : "";
        if (!raw) return "";

        try {
            const checkoutUrl = new URL(raw, window.location.origin);
            return checkoutUrl.protocol === "http:" || checkoutUrl.protocol === "https:"
                ? checkoutUrl.href
                : "";
        } catch (_) {
            return "";
        }
    }

    async function payCommitment(button: HTMLButtonElement | null): Promise<void> {
        if (!accessToken) return;
        const checkoutWindow = window.open("", "_blank", "noopener");
        await withStandaloneButton(button, button?.dataset.busyText || "Creating invoice...", async () => {
            const result = await postJson(`${API}/commitment-payment`, { accessToken });
            participantState = result;
            publicState = result.public;
            const checkoutLink = normalizeCheckoutLink(result.commitment && result.commitment.checkoutLink);
            if (!checkoutLink) throw new Error("The payment invoice did not return a usable checkout link.");
            if (checkoutWindow) {
                checkoutWindow.location = checkoutLink;
            } else {
                window.location.href = checkoutLink;
            }
            renderAll();
            setCommitmentStatus("Waiting for payment confirmation...", false);
            startCommitmentPaymentPolling();
        }, err => {
            if (checkoutWindow) checkoutWindow.close();
            setCommitmentStatus(messageOf(err), true);
        });
    }

    async function checkCommitmentPayment(button: HTMLButtonElement | null, options: CommitmentPaymentCheckOptions = {}): Promise<void> {
        if (!accessToken) return;
        if (options.showWaiting) setCommitmentStatus("Waiting for payment confirmation...", false);
        await withStandaloneButton(button, "Checking...", async () => {
            const result = await postJson(`${API}/commitment-payment/status`, { accessToken });
            participantState = result;
            publicState = result.public;
            if (!hasCommitmentBlock(result)) {
                commitmentPaymentPollRun += 1;
                participantNotice = { message: "Payment confirmed. You're unlocked.", isError: false };
                participantActiveTab = null;
                participantTabManual = false;
                renderAll();
                return;
            }
            renderAll();
            setCommitmentStatus(commitmentWaitingMessage(result.commitment, options.finalWaitingMessage), true);
        }, err => setCommitmentStatus(messageOf(err), true));
    }

    function startCommitmentPaymentPolling() {
        const run = ++commitmentPaymentPollRun;
        COMMITMENT_PAYMENT_POLL_DELAYS_MS.forEach((delay, index) => {
            window.setTimeout(() => {
                if (run !== commitmentPaymentPollRun || !hasCommitmentBlock(participantState)) return;
                checkCommitmentPayment(null, {
                    finalWaitingMessage: index === COMMITMENT_PAYMENT_POLL_DELAYS_MS.length - 1
                        ? "Still waiting. This can take a minute."
                        : ""
                });
            }, delay);
        });
    }

    function commitmentWaitingMessage(commitment: CommitmentState, fallback = ""): string {
        const status = String(commitment?.invoiceStatus || "").trim();
        const normalized = status.toLowerCase();
        if (["expired", "failed", "invalid"].includes(normalized)) {
            return "That payment attempt expired. Try again when ready.";
        }

        return fallback || "Waiting for payment confirmation...";
    }

    async function withStandaloneButton(button: HTMLButtonElement | null, busyText: string, work: ButtonWork, onError?: ErrorHandler): Promise<void> {
        if (button && (button.disabled || button.getAttribute("aria-busy") === "true")) return;

        const original = button ? button.innerHTML : "";
        if (button) {
            button.disabled = true;
            button.setAttribute("aria-busy", "true");
            button.innerHTML = `<i class="fas fa-spinner fa-spin" aria-hidden="true"></i>${busyText}`;
        }

        try {
            await work();
        } catch (err) {
            if (onError) onError(err);
        } finally {
            if (button) {
                button.disabled = false;
                button.removeAttribute("aria-busy");
                button.innerHTML = original;
            }
        }
    }

    function setCommitmentStatus(message: string, isError: boolean): void {
        const status = document.getElementById("lmxCommitmentStatus");
        if (!status) return;
        status.textContent = message || "";
        status.classList.toggle("error", !!isError);
        status.classList.toggle("success", !!message && !isError);
    }

    function renderProfileIdentity(participant: ParticipantSummary): void {
        const container = document.getElementById("lmxProfileIdentity");
        if (!container) return;

        const linkedAthlete = !!(participant.athleteSlug || participant.athleteUrl);
        const name = participant.displayName || "Participant";
        const badge = linkedAthlete ? "Longevity athlete" : "Challenge username";
        const identity = participant.athleteUrl
            ? `<a href="${escAttr(participant.athleteUrl)}">${esc(name)}</a>`
            : `<strong>${esc(name)}</strong>`;
        container.innerHTML = `
            <i class="fas ${linkedAthlete ? "fa-ranking-star" : "fa-user"}" aria-hidden="true"></i>
            <span>${identity}<em>${esc(badge)}</em></span>`;
    }

    function renderProfilePictureControls(participant: ParticipantSummary): void {
        const field = document.getElementById("lmxProfilePictureField");
        const preview = document.getElementById("lmxProfilePicturePreview");
        const image = optionalElement("lmxProfilePictureImage", HTMLImageElement);
        if (!field || !preview || !image) return;

        const canUpload = !(participant.athleteSlug || participant.athleteUrl);
        field.classList.toggle("lmx-hidden", !canUpload);
        if (!canUpload) return;

        const profileImage = String(participant.profileImageUrl || "").trim();
        preview.classList.toggle("placeholder", !profileImage);
        preview.setAttribute("aria-hidden", profileImage ? "false" : "true");
        image.src = profileImage || ATHLETE_PLACEHOLDER_IMAGE;
        image.alt = profileImage ? `${participant.displayName || "Participant"} profile picture` : "";
    }

    function renderParticipantCalls(calls: ParticipantCall[], callSelectionClosesAtUtc: string): void {
        const container = document.getElementById("lmxParticipantCalls");
        if (!container) return;
        const visibleCalls = (calls || [])
            .filter(call => !isParticipantCallDone(call))
            .sort((a, b) => getCallStartsAtMs(a) - getCallStartsAtMs(b))
            .slice(0, 1);
        if (!visibleCalls.length) {
            container.innerHTML = "";
            updateCallCountdowns();
            return;
        }

        container.innerHTML = visibleCalls.map(call => {
            const timeZoneId = getParticipantTimeZone();
            const when = call.selectedSlot ? formatCallWhen(call.selectedSlot.startsAtUtc, timeZoneId) : { primary: pendingCallTimeLabel(callSelectionClosesAtUtc, timeZoneId), secondary: "" };
            const countdown = call.selectedSlot
                ? callCountdownHtml(call.selectedSlot.startsAtUtc)
                : "";
            const link = call.videoCallUrl
                ? `<a class="lmx-call-link" href="${escAttr(call.videoCallUrl)}" target="_blank" rel="noopener">Google Meet</a>`
                : "";
            return `<div class="lmx-call-group">
                <div class="lmx-call-main">
                    <div class="lmx-call-copy">
                        <strong><i class="fas fa-users lmx-call-title-icon" aria-hidden="true"></i>Next community call</strong>
                        <span class="lmx-call-when"><b>${esc(when.primary)}</b>${when.secondary ? `<small>${esc(when.secondary)}</small>` : ""}</span>
                    </div>
                </div>
                <div class="lmx-call-side">
                    ${countdown}
                    ${link}
                </div>
            </div>`;
        }).join("");
        updateCallCountdowns();
    }

    function callCountdownHtml(startsAtUtc: string): string {
        const countdown = formatCallCountdown(startsAtUtc);
        if (!countdown.value) return "";
        return `<span class="lmx-call-countdown" data-call-countdown data-call-starts-at="${escAttr(startsAtUtc)}">
            <small>${esc(countdown.label)}</small>
            <b>${esc(countdown.value)}</b>
        </span>`;
    }

    function startCallCountdownTimer() {
        if (callCountdownTimer) return;
        callCountdownTimer = window.setInterval(updateCallCountdowns, 60000);
    }

    function updateCallCountdowns() {
        document.querySelectorAll<HTMLElement>("[data-call-countdown]").forEach(element => {
            const countdown = formatCallCountdown(element.dataset.callStartsAt || "");
            element.classList.toggle("live", countdown.label === "Live now");
            element.classList.toggle("lmx-hidden", !countdown.value);
            const label = element.querySelector("small");
            const value = element.querySelector("b");
            if (label) label.textContent = countdown.label;
            if (value) value.textContent = countdown.value;
        });
    }

    function formatCallCountdown(startsAtUtc: string): { label: string; value: string } {
        const startsAtMs = Date.parse(startsAtUtc);
        if (!Number.isFinite(startsAtMs)) return { label: "", value: "" };
        const remainingMinutes = Math.ceil((startsAtMs - Date.now()) / 60000);
        if (remainingMinutes <= 0) return { label: "Live now", value: "Join" };
        if (remainingMinutes < 60) return { label: "Starts in", value: `${remainingMinutes}m` };

        const days = Math.floor(remainingMinutes / 1440);
        const hours = Math.floor((remainingMinutes % 1440) / 60);
        const minutes = remainingMinutes % 60;
        if (days > 0) return { label: "Starts in", value: hours > 0 ? `${days}d ${hours}h` : `${days}d` };
        return { label: "Starts in", value: minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h` };
    }

    function isParticipantCallDone(call: ParticipantCall): boolean {
        const startsAtMs = getCallStartsAtMs(call);
        return Number.isFinite(startsAtMs) && startsAtMs + CALL_ACTIVE_WINDOW_MS < Date.now();
    }

    function getCallStartsAtMs(call: ParticipantCall): number {
        const startsAtMs = call && call.selectedSlot ? Date.parse(call.selectedSlot.startsAtUtc) : NaN;
        return Number.isFinite(startsAtMs) ? startsAtMs : Number.MAX_SAFE_INTEGER;
    }

    function renderBoard(state: PublicState): void {
        const board = requiredElement("lmxBoard", HTMLElement);
        if (isPreStartSignup(state)) {
            renderRosterBoard(board, state);
            return;
        }

        const publicViewer = !participantState;
        board.className = publicViewer ? "lmx-board public" : "lmx-board";
        const dayCount = (state.days || []).length || state.durationDays || 14;
        setBoardDayColumns(board, dayCount, false);
        updateInactiveToggle(state);
        const dayHeaders = (state.days || []).map(day => `<div class="lmx-cell">${day.challengeDay}</div>`).join("");
        const leaderboardRows = splitLeaderboardRows(state);
        const rows = leaderboardRows.visible.map((row, index) => {
            const name = row.athleteUrl
                ? `<a href="${escAttr(row.athleteUrl)}">${esc(row.displayName)}</a>`
                : `<span>${esc(row.displayName)}</span>`;
            const participant = participantNameHtml(row, name, index + 1);
            const cells = (row.cells || []).map(cell => {
                if (!cell.checkedIn) return `<div class="lmx-cell empty" data-day="${escAttr(cell.challengeDay)}" title="Day ${cell.challengeDay}"></div>`;
                if (cell.countsForScore === false) {
                    return practiceDayCellHtml(cell);
                }
                return scoredDayCellHtml(cell);
            }).join("");
            return `<div class="lmx-board-row${row.challengeInactive ? " inactive" : ""}" role="row">
                <div class="lmx-name" role="cell">${participant}</div>
                <div class="lmx-number" role="cell" data-label="Score">${row.totalPoints}</div>
                <div class="lmx-cell-strip" role="cell" aria-label="Daily scores">${cells}</div>
            </div>`;
        }).join("");

        board.innerHTML = `<div class="lmx-board-row header" role="row">
            <div class="lmx-name lmx-sticky-heading" role="columnheader">Participant</div>
            <div class="lmx-number lmx-sticky-heading" role="columnheader">Score</div>
            <div class="lmx-cell-strip lmx-header-days" role="presentation">${dayHeaders}</div>
        </div>${rows || emptyBoardRow(dayCount, leaderboardRows.inactive.length)}`;
    }

    function practiceDayCellHtml(cell: DayCell): string {
        const breakdown = habitBreakdown(cell);
        const title = practiceCellTitle(cell, breakdown);
        if (!breakdown.length) {
            return `<div class="lmx-cell practice" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(title)}" aria-label="${escAttr(title)}"><i class="fa fa-rocket" aria-hidden="true"></i></div>`;
        }

        const marks = breakdown.map(item => `<span class="${habitMarkClass(item.value)}" title="${escAttr(`${item.label} ${item.value}/2`)}" aria-hidden="true">${esc(item.short)}</span>`).join("");
        return `<div class="lmx-cell lmx-cell-breakdown practice" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(title)}" aria-label="${escAttr(title)}">
            <span class="lmx-cell-score"><i class="fa fa-rocket" aria-hidden="true"></i></span>
            <span class="lmx-habit-marks">${marks}</span>
        </div>`;
    }

    function scoredDayCellHtml(cell: DayCell): string {
        const score = typeof cell.score === "number" ? cell.score : 0;
        const breakdown = habitBreakdown(cell);
        const title = habitCellTitle(cell, score, breakdown);
        if (!breakdown.length) {
            const scoreClass = score >= 8 ? "score-high" : score >= 4 ? "score-mid" : "score-low";
            return `<div class="lmx-cell ${scoreClass}" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(title)}" aria-label="${escAttr(title)}">${score}</div>`;
        }

        const rawScore = breakdown.reduce((sum, item) => sum + item.value, 0);
        const scoreClass = rawScore >= 6 ? "score-high" : rawScore >= 3 ? "score-mid" : "score-low";
        const marks = breakdown.map(item => `<span class="${habitMarkClass(item.value)}" title="${escAttr(`${item.label} ${item.value}/2`)}" aria-hidden="true">${esc(item.short)}</span>`).join("");
        return `<div class="lmx-cell lmx-cell-breakdown ${scoreClass}" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(title)}" aria-label="${escAttr(title)}">
            <span class="lmx-cell-score">${score}</span>
            <span class="lmx-habit-marks">${marks}</span>
        </div>`;
    }

    function habitBreakdown(cell: DayCell): HabitBreakdownItem[] {
        const habits: ReadonlyArray<Omit<HabitBreakdownItem, "value">> = [
            { key: "sleep", label: "Sleep", short: "S" },
            { key: "exercise", label: "Exercise", short: "E" },
            { key: "nutrition", label: "Nutrition", short: "N" },
            { key: "vices", label: "Vices", short: "V" }
        ];
        const values = habits.map(habit => {
            const value = Number(cell[habit.key]);
            return Number.isFinite(value)
                ? { ...habit, value: Math.max(0, Math.min(2, value)) }
                : null;
        });

        return values.every(Boolean)
            ? values.filter((value): value is HabitBreakdownItem => value !== null)
            : [];
    }

    function habitCellTitle(cell: DayCell, score: number, breakdown: HabitBreakdownItem[]): string {
        if (!breakdown.length) return `Day ${cell.challengeDay}: ${score}`;

        const pieces = breakdown.map(item => `${item.label} ${item.value}/2`);
        const missed = breakdown.filter(item => item.value < 2).map(item => item.label.toLowerCase());
        const missedText = missed.length ? `. Missing: ${missed.join(", ")}` : ". Full day";
        return `Day ${cell.challengeDay}: ${score} points. ${pieces.join(", ")}${missedText}`;
    }

    function practiceCellTitle(cell: DayCell, breakdown: HabitBreakdownItem[]): string {
        if (!breakdown.length) return `Day ${cell.challengeDay}: practice check-in`;

        const pieces = breakdown.map(item => `${item.label} ${item.value}/2`);
        const missed = breakdown.filter(item => item.value < 2).map(item => item.label.toLowerCase());
        const missedText = missed.length ? `. Missing: ${missed.join(", ")}` : ". Full practice day";
        return `Day ${cell.challengeDay}: practice check-in. ${pieces.join(", ")}${missedText}`;
    }

    function habitMarkClass(value: number): string {
        if (value >= 2) return "lmx-habit-mark full";
        if (value > 0) return "lmx-habit-mark partial";
        return "lmx-habit-mark missed";
    }

    function renderRosterBoard(board: HTMLElement, state: PublicState): void {
        board.className = "lmx-board roster";
        const dayCount = (state.days || []).length || state.durationDays || 14;
        setBoardDayColumns(board, dayCount, true);
        updateInactiveToggle(state);
        const dayHeaders = (state.days || []).map(day => `<div class="lmx-cell">${day.challengeDay}</div>`).join("");
        const leaderboardRows = splitLeaderboardRows(state);
        const rows = leaderboardRows.visible.map((row, index) => {
            const name = row.athleteUrl
                ? `<a href="${escAttr(row.athleteUrl)}">${esc(row.displayName)}</a>`
                : `<span>${esc(row.displayName)}</span>`;
            const participant = participantNameHtml(row, name, index + 1);
            const cells = (row.cells || state.days || []).map(cell => `<div class="lmx-cell empty" data-day="${escAttr(cell.challengeDay)}" title="Day ${cell.challengeDay}"></div>`).join("");
            return `<div class="lmx-board-row lmx-roster-row${row.challengeInactive ? " inactive" : ""}" role="row">
                <div class="lmx-name" role="cell">${participant}</div>
                <div class="lmx-cell-strip" role="cell" aria-label="Challenge days">${cells}</div>
            </div>`;
        }).join("");

        board.innerHTML = `<div class="lmx-board-row lmx-roster-row header" role="row">
            <div class="lmx-name lmx-sticky-heading" role="columnheader">Participant</div>
            <div class="lmx-cell-strip lmx-header-days" role="presentation">${dayHeaders}</div>
        </div>${rows || emptyRosterRow(dayCount, leaderboardRows.inactive.length)}`;
    }

    function setBoardDayColumns(board: HTMLElement, dayCount: number, rosterMode: boolean): void {
        const count = Math.max(1, Math.trunc(Number(dayCount) || 14));
        board.style.setProperty("--lmx-day-columns", `repeat(${count}, 2.55rem)`);
        const stickyWidthRem = rosterMode ? 16 : 21.15;
        const gapCount = rosterMode ? count : count + 1;
        const rowPaddingRem = 0.7;
        const dayWidthRem = count * 2.55;
        const gapWidthRem = gapCount * 0.35;
        board.style.setProperty("--lmx-board-min-width", `${(stickyWidthRem + dayWidthRem + gapWidthRem + rowPaddingRem).toFixed(2)}rem`);
    }

    function splitLeaderboardRows(state: PublicState): { active: LeaderboardRow[]; inactive: LeaderboardRow[]; visible: LeaderboardRow[] } {
        const all = (state && state.leaderboard) || [];
        const active = all.filter(row => !row.challengeInactive);
        const inactive = all.filter(row => row.challengeInactive);
        return {
            active,
            inactive,
            visible: showInactiveLeaderboard ? [...active, ...inactive] : active
        };
    }

    function leaderboardScoringWindowDays(state: PublicState): number {
        const dayCount = (state?.days || []).length || state?.durationDays || LEADERBOARD_SCORING_WINDOW_DAYS;
        return Math.min(LEADERBOARD_SCORING_WINDOW_DAYS, Math.max(1, Math.trunc(Number(dayCount) || LEADERBOARD_SCORING_WINDOW_DAYS)));
    }

    function updateInactiveToggle(state: PublicState): void {
        const button = document.getElementById("lmxInactiveToggle");
        if (!button) return;
        const rows = splitLeaderboardRows(state);
        button.classList.toggle("lmx-hidden", rows.inactive.length === 0);
        button.setAttribute("aria-pressed", showInactiveLeaderboard ? "true" : "false");
        button.setAttribute("aria-label", showInactiveLeaderboard
            ? "Hide resting participants"
            : `Show resting participants (${rows.inactive.length})`);
        button.textContent = showInactiveLeaderboard ? "Hide resting" : `Show resting (${rows.inactive.length})`;
    }

    function scrollBoardToLatestDay() {
        const scroller = document.querySelector("#lmxBoardSection .lmx-board-scroll");
        if (!scroller) return;

        const scrollRight = () => {
            scroller.scrollLeft = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
        };

        requestAnimationFrame(() => {
            scrollRight();
            requestAnimationFrame(scrollRight);
            window.setTimeout(scrollRight, 120);
            window.setTimeout(scrollRight, 500);
            window.setTimeout(scrollRight, 1200);
        });

        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(scrollRight).catch(() => { });
        }

        if (window.ResizeObserver && boardScrollObservedElement !== scroller) {
            if (boardScrollObserver) boardScrollObserver.disconnect();
            boardScrollObservedElement = scroller;
            boardScrollObserver = new ResizeObserver(scrollRight);
            boardScrollObserver.observe(scroller);
            const board = document.getElementById("lmxBoard");
            if (board) boardScrollObserver.observe(board);
        }
    }

    function scrollDashboardToLatestDay() {
        const scroller = document.querySelector<HTMLElement>("#lmxTrack .lmx-dashboard-scroll");
        if (!scroller) return;

        const scrollCurrentDayIntoFocus = () => {
            const currentDay = scroller.querySelector<HTMLElement>(".lmx-dashboard-row-head .lmx-dashboard-day.today");
            if (!currentDay) {
                scroller.scrollLeft = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
                return;
            }

            const stickyColumn = scroller.querySelector<HTMLElement>(".lmx-dashboard-corner");
            const styles = getComputedStyle(scroller.querySelector(".lmx-dashboard-grid") || scroller);
            const gap = parseFloat(styles.getPropertyValue("--lmx-dashboard-gap")) || 0;
            const stickyWidth = (stickyColumn?.offsetWidth || 0) + gap;
            const availableWidth = Math.max(currentDay.offsetWidth, scroller.clientWidth - stickyWidth);
            const centered = currentDay.offsetLeft - stickyWidth - ((availableWidth - currentDay.offsetWidth) / 2);
            const maxScroll = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
            scroller.scrollLeft = Math.max(0, Math.min(maxScroll, centered));
        };

        requestAnimationFrame(() => {
            scrollCurrentDayIntoFocus();
            requestAnimationFrame(scrollCurrentDayIntoFocus);
            window.setTimeout(scrollCurrentDayIntoFocus, 120);
            window.setTimeout(scrollCurrentDayIntoFocus, 500);
        });

        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(scrollCurrentDayIntoFocus).catch(() => { });
        }

        if (window.ResizeObserver && dashboardScrollObservedElement !== scroller) {
            if (dashboardScrollObserver) dashboardScrollObserver.disconnect();
            dashboardScrollObservedElement = scroller;
            dashboardScrollObserver = new ResizeObserver(scrollCurrentDayIntoFocus);
            dashboardScrollObserver.observe(scroller);
            const dashboard = scroller.querySelector(".lmx-dashboard-grid");
            if (dashboard) dashboardScrollObserver.observe(dashboard);
        }
    }

    function emptyBoardRow(durationDays: number, hiddenInactiveCount: number): string {
        const hasHiddenInactive = hiddenInactiveCount > 0 && !showInactiveLeaderboard;
        const message = hasHiddenInactive ? "No active participants" : "No one has joined yet";
        const scoreLabel = hasHiddenInactive ? "No active score" : "No score yet";
        return `<div class="lmx-board-row" role="row">
            <div class="lmx-name" role="cell">
                <span class="lmx-empty-name">${esc(message)}</span>
            </div>
            <div class="lmx-number lmx-empty-score" role="cell" data-label="Score" aria-label="${escAttr(scoreLabel)}">-</div>
            <div class="lmx-cell-strip" role="cell" aria-label="Daily scores">${Array.from({ length: durationDays }, (_, index) => `<div class="lmx-cell empty" data-day="${index + 1}"></div>`).join("")}</div>
        </div>`;
    }

    function emptyRosterRow(durationDays: number, hiddenInactiveCount: number): string {
        const hasHiddenInactive = hiddenInactiveCount > 0 && !showInactiveLeaderboard;
        const message = hasHiddenInactive ? "No active participants" : "No one has joined yet";
        return `<div class="lmx-board-row lmx-roster-row" role="row">
            <div class="lmx-name" role="cell">
                <span class="lmx-empty-name">${esc(message)}</span>
            </div>
            <div class="lmx-cell-strip" role="cell" aria-label="Challenge days">${Array.from({ length: durationDays }, (_, index) => `<div class="lmx-cell empty" data-day="${index + 1}"></div>`).join("")}</div>
        </div>`;
    }

    function renderCheckIns(days: EligibleDay[], containerId = "lmxCheckinList", recentRemarks: ParticipantNote[] = []): void {
        const container = document.getElementById(containerId || "lmxCheckinList");
        if (!container) return;
        if (!days.length) {
            container.innerHTML = emptyCheckInHtml();
            return;
        }

        const orderedDays = [...days].sort((a, b) => a.challengeDay - b.challengeDay);
        const activeDay = pickActiveCheckInDay(orderedDays);
        const previousForm = container.querySelector<HTMLFormElement>(".lmx-checkin-card");
        if (previousForm) revokePendingNotePhotoUrls(checkInDayKey(previousForm));
        container.innerHTML = checkInSwitcherHtml(orderedDays, activeDay) + checkInCardHtml(activeDay, recentRemarks);
        container.querySelectorAll<HTMLButtonElement>(".lmx-checkin-switcher button").forEach(button => {
            button.addEventListener("click", () => {
                selectedCheckInDay = Number(button.dataset.day);
                renderCheckIns(orderedDays, containerId, recentRemarks);
            });
        });
        container.querySelectorAll<HTMLButtonElement>(".lmx-segmented button").forEach(button => {
            button.addEventListener("click", () => {
                const group = button.closest(".lmx-segmented");
                group?.querySelectorAll("button").forEach(item => item.setAttribute("aria-pressed", "false"));
                button.setAttribute("aria-pressed", "true");
                const form = button.closest("form");
                if (form) updateCheckInSaveState(form);
            });
        });
        container.querySelectorAll<HTMLFormElement>("form").forEach(form => {
            form.querySelector("textarea")?.addEventListener("input", () => updateCheckInSaveState(form));
            form.querySelector<HTMLButtonElement>("[data-photo-button]")?.addEventListener("click", () => {
                form.querySelector<HTMLInputElement>("input[data-note-photos]")?.click();
            });
            form.querySelector<HTMLInputElement>("input[data-note-photos]")?.addEventListener("change", event => {
                const input = event.currentTarget;
                if (!(input instanceof HTMLInputElement)) return;
                setPendingNotePhotos(form, Array.from(input.files || []));
                input.value = "";
                renderSelectedNotePhotoPreviews(form);
                updateCheckInSaveState(form);
            });
            renderSelectedNotePhotoPreviews(form);
            updateCheckInSaveState(form);
            form.addEventListener("submit", event => {
                event.preventDefault();
                submitCheckIn(form);
            });
        });
    }

    function pickActiveCheckInDay(days: EligibleDay[]): EligibleDay {
        const current = days.find(day => day.challengeDay === selectedCheckInDay);
        if (current) return current;

        const missing = [...days]
            .filter(day => !day.existing)
            .sort((a, b) => b.challengeDay - a.challengeDay)[0];
        const fallback = missing || days.at(-1);
        if (!fallback) throw new Error("At least one eligible check-in day is required.");
        selectedCheckInDay = fallback.challengeDay;
        return fallback;
    }

    function checkInSwitcherHtml(days: EligibleDay[], activeDay: EligibleDay): string {
        if (days.length < 2) return "";

        return `<div class="lmx-checkin-switcher" aria-label="Eligible check-in days">
            ${days.map(day => {
                const isActive = day.challengeDay === activeDay.challengeDay;
                const status = day.existing ? "Saved" : "Due";
                return `<button type="button" data-day="${day.challengeDay}" aria-pressed="${isActive ? "true" : "false"}">
                    <strong>Day ${day.challengeDay}</strong>
                    <span>${esc(formatShortDateLabel(day.date))}</span>
                    <em>${status}</em>
                </button>`;
            }).join("")}
        </div>`;
    }

    function checkInCardHtml(day: EligibleDay, recentRemarks: ParticipantNote[]): string {
        const existing: Partial<CheckInDraft> = day.existing || {};
        const saved = savedDays.has(day.challengeDay);
        const practice = day.countsForScore === false;
        const hasExisting = !!day.existing;
        const note = (existing.note || "").trim();
        const savedImages = Array.isArray(existing.images) ? existing.images : [];
        const savedImageHtml = savedImages.length
            ? `<div class="lmx-note-photo-grid saved" aria-label="Saved note photos">${savedImages.map((image, index) => notePhotoHtml(image, `${day.challengeDay}-${index}`)).join("")}</div>`
            : "";
        const photoSlotsLeft = Math.max(0, MAX_NOTE_PHOTOS - savedImages.length);
        const questions = QUESTIONS.map(q => {
            const current = typeof existing[q.key] === "number" ? existing[q.key] : 1;
            const buttons = ANSWERS.map(answer => `<button type="button" data-value="${answer.value}" aria-pressed="${answer.value === current ? "true" : "false"}">${answer.label}</button>`).join("");
            return `<div class="lmx-question" data-key="${q.key}">
                <div class="lmx-question-label"><i class="fas ${q.icon}" aria-hidden="true"></i><span>${q.text}</span></div>
                <div class="lmx-segmented">${buttons}</div>
            </div>`;
        }).join("");

        const originalAttrs = QUESTIONS
            .map(q => `data-original-${q.key}="${typeof existing[q.key] === "number" ? existing[q.key] : 1}"`)
            .join(" ");

        return `<form class="lmx-checkin-card" data-day="${day.challengeDay}" data-saved="${hasExisting ? "true" : "false"}" ${originalAttrs} data-original-note="${escAttr(note)}">
            <h3>Day ${day.challengeDay} <span class="lmx-phase">${practice ? `Practice check-in - ${esc(formatCheckInDate(day.date))}` : esc(formatCheckInDate(day.date))}</span></h3>
            ${practice ? `<div class="lmx-practice-note"><strong>Practice check-in.</strong><span>Counts for checked-in days and streak, not points.</span></div>` : ""}
            ${questions}
            <div class="lmx-field">
                <label for="lmx-note-${day.challengeDay}">Remarks <span>optional</span></label>
                <textarea id="lmx-note-${day.challengeDay}" maxlength="240" placeholder="Visible publicly">${esc(note)}</textarea>
            </div>
            <div class="lmx-field lmx-note-photo-field" data-photo-slots="${photoSlotsLeft}">
                <span class="lmx-label">Photos <span>optional</span></span>
                ${savedImageHtml}
                <div class="lmx-note-photo-picker">
                    <button class="lmx-button secondary" type="button" data-photo-button${photoSlotsLeft <= 0 ? " disabled" : ""}>
                        <i class="fas fa-images" aria-hidden="true"></i>
                        Add photos
                    </button>
                    <input id="lmx-note-photos-${day.challengeDay}" type="file" accept="image/*,.heic,.heif" multiple data-note-photos ${photoSlotsLeft <= 0 ? "disabled" : ""}>
                    <span class="lmx-photo-count" data-photo-count>${photoSlotsLeft <= 0 ? "Photo limit reached" : `${photoSlotsLeft} slots left`}</span>
                </div>
                <div class="lmx-note-photo-grid pending" data-photo-previews></div>
            </div>
            <button class="lmx-button" type="submit"${hasExisting ? " disabled" : ""}>
                <i class="fas fa-check" aria-hidden="true"></i>
                Save
            </button>
            <div class="lmx-status${saved || day.existing ? " success" : ""}">${saved || day.existing ? SAVED_CHECKIN_TEXT : ""}</div>
            ${recentRemarksHtml(recentRemarks)}
        </form>`;
    }

    function recentPublicRemarks(state: ParticipantState): ParticipantNote[] {
        const notes = Array.isArray(state?.public?.notes) ? state.public.notes : [];
        return notes
            .filter(note => String(note?.note || "").trim())
            .slice(0, RECENT_REMARK_LIMIT);
    }

    function recentRemarksHtml(notes: ParticipantNote[]): string {
        const remarks = (Array.isArray(notes) ? notes : [])
            .filter(note => String(note?.note || "").trim())
            .slice(0, RECENT_REMARK_LIMIT);
        if (!remarks.length) return "";

        return `<section class="lmx-recent-remarks" aria-label="Recent public remarks">
            <strong>Recent remarks</strong>
            ${remarks.map(note => {
                const noteText = String(note.note || "").trim();
                const date = note.date ? ` · ${formatShortDateLabel(note.date)}` : "";
                return `<article class="lmx-recent-remark">
                    <strong>${esc(note.displayName)} · Day ${esc(note.challengeDay)}${esc(date)}</strong>
                    <p>${esc(noteText)}</p>
                </article>`;
            }).join("")}
        </section>`;
    }

    function notePhotoHtml(image: CheckInImage, key: string): string {
        const url = String(image && image.url || "").trim();
        if (!url) return "";
        const width = Number(image.width) || "";
        const height = Number(image.height) || "";
        return `<a class="lmx-note-photo" href="${escAttr(url)}" target="_blank" rel="noopener" aria-label="Open note photo">
            <img src="${escAttr(url)}" alt="" loading="lazy" decoding="async" width="${escAttr(width)}" height="${escAttr(height)}" data-photo-key="${escAttr(key)}">
        </a>`;
    }

    function setPendingNotePhotos(form: HTMLFormElement, files: File[]): void {
        const key = checkInDayKey(form);
        const slots = Number(form.querySelector<HTMLElement>(".lmx-note-photo-field")?.dataset.photoSlots || MAX_NOTE_PHOTOS);
        const photos = files
            .filter(file => /^image\//i.test(String(file.type || "")) || /\.(heic|heif)$/i.test(file.name || ""))
            .slice(0, Math.max(0, slots));

        revokePendingNotePhotoUrls(key);
        if (photos.length) pendingNotePhotos.set(key, photos);
        else pendingNotePhotos.delete(key);
    }

    function removePendingNotePhoto(form: HTMLFormElement, index: number): void {
        const key = checkInDayKey(form);
        const photos = getPendingNotePhotos(form).filter((_, photoIndex) => photoIndex !== index);
        revokePendingNotePhotoUrls(key);
        if (photos.length) pendingNotePhotos.set(key, photos);
        else pendingNotePhotos.delete(key);
        const input = form.querySelector<HTMLInputElement>("input[data-note-photos]");
        if (input && !photos.length) input.value = "";
        renderSelectedNotePhotoPreviews(form);
        updateCheckInSaveState(form);
    }

    function renderSelectedNotePhotoPreviews(form: HTMLFormElement): void {
        const previews = form.querySelector("[data-photo-previews]");
        const count = form.querySelector("[data-photo-count]");
        if (!previews) return;

        const key = checkInDayKey(form);
        const photos = getPendingNotePhotos(form);
        const slots = Number(form.querySelector<HTMLElement>(".lmx-note-photo-field")?.dataset.photoSlots || MAX_NOTE_PHOTOS);
        revokePendingNotePhotoUrls(key);
        const urls = photos.map(photo => URL.createObjectURL(photo));
        if (urls.length) pendingNotePhotoUrls.set(key, urls);

        previews.innerHTML = photos.map((photo, index) => `<span class="lmx-note-photo pending-item">
            <img src="${escAttr(urls[index])}" alt="" loading="lazy" decoding="async">
            <button type="button" class="lmx-note-photo-remove" data-remove-photo="${index}" title="Remove photo" aria-label="Remove photo">
                <i class="fas fa-xmark" aria-hidden="true"></i>
            </button>
        </span>`).join("");

        previews.querySelectorAll<HTMLElement>("[data-remove-photo]").forEach(button => {
            button.addEventListener("click", () => removePendingNotePhoto(form, Number(button.dataset.removePhoto)));
        });

        if (count) {
            const remaining = Math.max(0, slots - photos.length);
            count.textContent = photos.length
                ? `${photos.length} selected · ${remaining} slots left`
                : (slots <= 0 ? "Photo limit reached" : `${slots} slots left`);
        }
    }

    function clearPendingNotePhotos(challengeDay: number): void {
        const key = String(challengeDay);
        revokePendingNotePhotoUrls(key);
        pendingNotePhotos.delete(key);
    }

    function revokePendingNotePhotoUrls(key: string): void {
        (pendingNotePhotoUrls.get(key) || []).forEach(url => URL.revokeObjectURL(url));
        pendingNotePhotoUrls.delete(key);
    }

    function getPendingNotePhotos(form: HTMLFormElement): File[] {
        return pendingNotePhotos.get(checkInDayKey(form)) || [];
    }

    function checkInDayKey(form: HTMLFormElement): string {
        return String(Number(form?.dataset.day || 0));
    }

    function emptyCheckInHtml() {
        if (!participantState) return "";
        const phase = participantState.public.phase;

        if (phase === "signup" || phase === "roster") {
            const firstOpen = datePlusDays(participantState.public.startDate, 1);
            return `<div class="lmx-empty-state">
                <i class="fas fa-circle-check" aria-hidden="true"></i>
                <strong>You're in.</strong>
                <span>Your first check-in email arrives ${esc(formatDateLabel(firstOpen))}. Nothing is due before then.</span>
            </div>`;
        }

        return `<div class="lmx-empty-state compact">
            <i class="fas fa-circle-check" aria-hidden="true"></i>
            <strong>Nothing due.</strong>
        </div>`;
    }

    function selectQuoteBucket(draft: CheckInFormDraft): QuoteBucket | null {
        const values = QUESTIONS.map(question => ({
            key: question.key,
            value: clampHabitValue(draft?.[question.key])
        }));
        if (values.every(item => item.value === 2)) return null;

        const minValue = Math.min(...values.map(item => item.value));
        const worst = values.filter(item => item.value === minValue);
        const soleWorst = worst[0];
        return worst.length === 1 && soleWorst ? soleWorst.key : "mindset";
    }

    async function showRandomCheckInQuote(bucket: QuoteBucket): Promise<void> {
        const quote = pickQuote(bucket);
        if (!quote) return;

        const token = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
        showCheckInQuoteDialog(quote, null, token);

        try {
            await loadAthleteDirectory();
        } catch (_) {
        }

        const athlete = findQuoteAthlete(quote);
        updateCheckInQuoteDialogRank(quote, computeQuoteAthleteBestRank(athlete), token);
    }

    function pickQuote(bucket: QuoteBucket): CheckInQuote | null {
        const safeBucket: QuoteBucket = QUOTE_BUCKETS.includes(bucket) ? bucket : "mindset";
        const rows = LMX_QUOTES[safeBucket] || [];
        if (!rows.length) return null;

        const row = rows[Math.floor(Math.random() * rows.length)];
        if (!row) return null;
        return {
            bucket: safeBucket,
            text: row[0],
            athleteName: row[1],
            athleteSlug: row[2],
            youtubeUrl: row[3]
        };
    }

    function findQuoteAthlete(quote: CheckInQuote): QuoteAthlete | AthleteOption | null {
        const slug = normalizeAthleteSlug(quote?.athleteSlug);
        if (!slug) return null;
        return quoteAthleteResults.find(athlete => normalizeAthleteSlug(athlete.slug) === slug)
            || athleteDirectory.find(athlete => normalizeAthleteSlug(athlete.slug) === slug)
            || null;
    }

    function showCheckInQuoteDialog(quote: CheckInQuote, bestRank: QuoteRankCandidateInput | null, token: string): void {
        const dialog = ensureCheckInQuoteDialog();
        const text = dialog.querySelector("#lmxQuoteDialogText");
        const source = dialog.querySelector("#lmxQuoteDialogSource");
        const portrait = dialog.querySelector("#lmxQuoteDialogPortrait");
        const ok = dialog.querySelector<HTMLButtonElement>("#lmxQuoteDialogOk");
        if (!text || !source || !portrait || !ok) return;

        quoteDialogLastFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        dialog.dataset.quoteToken = token || "";
        dialog.dataset.quoteBucket = quote.bucket || "";
        text.textContent = quote.text || "";
        source.innerHTML = renderCheckInQuoteSourceHtml(quote, bestRank);
        portrait.innerHTML = renderCheckInQuotePortraitHtml(quote, findQuoteAthlete(quote));
        dialog.hidden = false;
        document.body.classList.add("lmx-quote-open");
        requestAnimationFrame(() => ok.focus({ preventScroll: true }));
    }

    function ensureCheckInQuoteDialog(): HTMLElement {
        let dialog = document.getElementById("lmxQuoteDialog");
        if (dialog) return dialog;

        document.body.insertAdjacentHTML("beforeend", `
            <div id="lmxQuoteDialog" class="lmx-quote-dialog" role="dialog" aria-modal="true" aria-label="Challenge quote" hidden>
                <div class="lmx-quote-dialog-backdrop" aria-hidden="true"></div>
                <div class="lmx-quote-dialog-card">
                    <div class="lmx-quote-dialog-body">
                        <div id="lmxQuoteDialogPortrait" class="lmx-quote-portrait-shell"></div>
                        <div class="lmx-quote-dialog-main">
                            <blockquote id="lmxQuoteDialogText"></blockquote>
                            <div id="lmxQuoteDialogSource" class="lmx-quote-source"></div>
                            <div class="lmx-quote-dialog-actions">
                                <button id="lmxQuoteDialogOk" class="lmx-button" type="button">
                                    <i class="fas fa-check" aria-hidden="true"></i>
                                    <span>OK</span>
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>`);
        dialog = requiredElement("lmxQuoteDialog", HTMLElement);
        const ok = requiredButton("lmxQuoteDialogOk");
        ok.addEventListener("click", closeCheckInQuoteDialog);
        dialog.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                event.preventDefault();
                closeCheckInQuoteDialog();
                return;
            }
            if (event.key !== "Tab") return;

            const focusable = getDialogFocusableElements(dialog);
            if (!focusable.length) {
                event.preventDefault();
                return;
            }

            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last?.focus({ preventScroll: true });
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first?.focus({ preventScroll: true });
            }
        });
        return dialog;
    }

    function updateCheckInQuoteDialogRank(quote: CheckInQuote, bestRank: QuoteRankCandidateInput | null, token: string): void {
        const dialog = document.getElementById("lmxQuoteDialog");
        if (!dialog || dialog.hidden || dialog.dataset.quoteToken !== token) return;

        const source = dialog.querySelector("#lmxQuoteDialogSource");
        if (source) source.innerHTML = renderCheckInQuoteSourceHtml(quote, bestRank);
        const portrait = dialog.querySelector("#lmxQuoteDialogPortrait");
        if (portrait) portrait.innerHTML = renderCheckInQuotePortraitHtml(quote, findQuoteAthlete(quote));
    }

    function closeCheckInQuoteDialog() {
        const dialog = document.getElementById("lmxQuoteDialog");
        if (dialog) {
            dialog.hidden = true;
            delete dialog.dataset.quoteToken;
            delete dialog.dataset.quoteBucket;
        }
        document.body.classList.remove("lmx-quote-open");
        try {
            quoteDialogLastFocus?.focus?.({ preventScroll: true });
        } catch (_) {
        }
        quoteDialogLastFocus = null;
    }

    function renderCheckInQuoteSourceHtml(quote: CheckInQuote, bestRank: QuoteRankCandidateInput | null): string {
        const athleteSlug = normalizeAthleteSlug(quote.athleteSlug);
        const athleteName = quote.athleteName || "Longevity athlete";
        const athlete = athleteSlug
            ? `<a href="/athlete/${escAttr(athleteSlug)}" target="_blank" rel="noopener noreferrer">${esc(athleteName)}</a>`
            : `<span>${esc(athleteName)}</span>`;
        const rank = formatQuoteRankText(bestRank);
        const podcast = quote.youtubeUrl
            ? `<a class="lmx-quote-podcast-link" href="${escAttr(quote.youtubeUrl)}" target="_blank" rel="noopener noreferrer" aria-label="Open podcast episode with ${escAttr(athleteName)}" title="Podcast"><i class="fa fa-microphone" aria-hidden="true"></i><span>Podcast</span></a>`
            : "";
        return [
            `<span class="lmx-quote-athlete">${athlete}</span>`,
            rank ? `<span class="lmx-quote-rank">${esc(rank)}</span>` : "",
            podcast
        ].filter(Boolean).join("");
    }

    function renderCheckInQuotePortraitHtml(quote: CheckInQuote, athlete: QuoteAthlete | AthleteOption | null): string {
        const athleteName = quote.athleteName || athlete?.name || "Longevity athlete";
        const image = getQuoteAthleteProfileImage(athlete);
        const hasProfileImage = !!image;
        const imageUrl = image || ATHLETE_PLACEHOLDER_IMAGE;
        const placeholderClass = hasProfileImage ? "" : " placeholder";
        const alt = hasProfileImage ? `${athleteName} profile picture` : "";
        return `<div class="lmx-quote-portrait${placeholderClass}">
            <img src="${escAttr(imageUrl)}" alt="${escAttr(alt)}" loading="lazy" decoding="async">
            <span class="lmx-quote-portrait-badge" aria-hidden="true">
                <i class="fas ${getQuoteDialogIconClass(quote.bucket)}"></i>
            </span>
        </div>`;
    }

    function getQuoteAthleteProfileImage(athlete: QuoteAthlete | AthleteOption | null): string {
        const image = String(athlete?.profilePic || "").trim();
        return isPlaceholderProfileImage(image) ? "" : image;
    }

    function getDialogFocusableElements(dialog: HTMLElement): HTMLElement[] {
        return Array.from(dialog.querySelectorAll<HTMLElement>("a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])"))
            .filter(element => element.offsetParent !== null);
    }

    function getQuoteDialogIconClass(bucket: QuoteBucket): string {
        switch (bucket) {
            case "sleep": return "fa-moon";
            case "exercise": return "fa-dumbbell";
            case "nutrition": return "fa-bowl-food";
            case "vices": return "fa-shield-halved";
            case "mindset": return "fa-brain";
            default: return "fa-quote-left";
        }
    }

    function formatQuoteRankText(bestRank: QuoteRankCandidateInput | null): string {
        if (!bestRank) return "";
        const rank = toFiniteNumber(bestRank.rank);
        if (rank === null) return "";
        const label = String(bestRank.leagueLabel || bestRank.leagueName || "Ultimate League").trim();
        return `#${Math.trunc(rank)} in ${label || "Ultimate League"}`;
    }

    async function submitCheckIn(form: HTMLFormElement): Promise<void> {
        if (!accessToken) return;
        const currentAccessToken = accessToken;
        if (!hasCheckInChanged(form)) return;
        await withButton(form.querySelector<HTMLButtonElement>("button[type='submit']"), async () => {
            const draft = collectCheckInDraft(form);
            const quoteBucket = selectQuoteBucket(draft);
            const notePhotos = getPendingNotePhotos(form);
            const payload: CheckInPayload = {
                accessToken: currentAccessToken,
                challengeDay: Number(form.dataset.day),
                sleep: draft.sleep,
                exercise: draft.exercise,
                nutrition: draft.nutrition,
                vices: draft.vices,
                note: draft.note || null
            };
            const result = notePhotos.length
                ? await postCheckInWithPhotos(payload, notePhotos)
                : await postJson(`${API}/check-in`, payload);
            savedDays.add(payload.challengeDay);
            clearPendingNotePhotos(payload.challengeDay);
            participantState = result;
            publicState = result.public;
            const nextMissing = getPendingCheckInDays(participantState)
                .sort((a, b) => b.challengeDay - a.challengeDay)[0];
            selectedCheckInDay = nextMissing ? nextMissing.challengeDay : payload.challengeDay;
            renderAll();
            if (quoteBucket) void showRandomCheckInQuote(quoteBucket);
        }, "Saving...");
    }

    async function postCheckInWithPhotos(payload: CheckInPayload, photos: File[]): Promise<ParticipantState> {
        const formData = new FormData();
        formData.append("accessToken", payload.accessToken);
        formData.append("challengeDay", String(payload.challengeDay));
        formData.append("sleep", String(payload.sleep));
        formData.append("exercise", String(payload.exercise));
        formData.append("nutrition", String(payload.nutrition));
        formData.append("vices", String(payload.vices));
        if (payload.note) formData.append("note", payload.note);

        const prepared = await Promise.all(photos.map(prepareNotePhotoFile));
        prepared.forEach((photo, index) => {
            formData.append("notePhotos", photo, photo.name || `check-in-photo-${index + 1}.webp`);
        });

        return postForm(`${API}/check-in`, formData);
    }

    function collectCheckInDraft(form: HTMLFormElement): CheckInFormDraft {
        const readHabit = (key: HabitKey): number => {
            const pressed = form.querySelector<HTMLButtonElement>(`.lmx-question[data-key="${key}"] button[aria-pressed="true"]`);
            return Number(pressed ? pressed.dataset.value : 1);
        };
        const draft: CheckInFormDraft = {
            note: form.querySelector<HTMLTextAreaElement>("textarea")?.value.trim() || "",
            sleep: readHabit("sleep"),
            exercise: readHabit("exercise"),
            nutrition: readHabit("nutrition"),
            vices: readHabit("vices")
        };
        return draft;
    }

    function hasCheckInChanged(form: HTMLFormElement): boolean {
        if (!form || form.dataset.saved !== "true") return true;

        const draft = collectCheckInDraft(form);
        if (getPendingNotePhotos(form).length > 0) return true;
        if ((form.dataset.originalNote || "") !== draft.note) return true;

        return QUESTIONS.some(q => Number(form.dataset[`original${capitalize(q.key)}`]) !== draft[q.key]);
    }

    function updateCheckInSaveState(form: HTMLFormElement): void {
        if (!form) return;

        const button = form.querySelector<HTMLButtonElement>("button[type='submit']");
        const status = form.querySelector(".lmx-status");
        const changed = hasCheckInChanged(form);

        if (button) button.disabled = !changed;
        if (status && form.dataset.saved === "true") {
            status.textContent = changed ? "" : SAVED_CHECKIN_TEXT;
            status.classList.remove("error");
            status.classList.toggle("success", !changed);
        }
    }

    async function uploadProfilePicture(file: File, input: HTMLInputElement): Promise<void> {
        if (!accessToken) return;

        const button = optionalElement("lmxProfilePictureButton", HTMLButtonElement);
        input.disabled = true;
        if (button) button.disabled = true;
        let shouldFocusRetry = false;
        setStatus("lmxProfilePictureStatus", "Uploading...", false);
        try {
            const uploadFile = await prepareProfilePictureFile(file);
            const formData = new FormData();
            formData.append("accessToken", accessToken);
            formData.append("profilePicture", uploadFile, uploadFile.name || "profile-picture.jpg");

            const result = await postForm(`${API}/profile-picture`, formData);
            participantState = result;
            publicState = result.public;
            renderAll();
            setStatus("lmxProfilePictureStatus", "Uploaded.", false);
        } catch (err) {
            shouldFocusRetry = true;
            setStatus("lmxProfilePictureStatus", messageOf(err), true);
        } finally {
            input.disabled = false;
            if (button) button.disabled = false;
            input.value = "";
            if (shouldFocusRetry) button?.focus();
        }
    }

    async function prepareProfilePictureFile(file: File): Promise<File> {
        const type = String(file.type || "");
        const isServerPreferred = /^image\/(jpeg|png|webp)$/i.test(type);
        const shouldNormalize = file.size > 1024 * 1024 || !isServerPreferred;
        if (!shouldNormalize) return file;

        try {
            const bitmap = await loadProfileBitmap(file);
            const maxDimension = 1600;
            const scale = Math.min(1, maxDimension / Math.max(bitmap.width, bitmap.height));
            const width = Math.max(1, Math.round(bitmap.width * scale));
            const height = Math.max(1, Math.round(bitmap.height * scale));
            const canvas = document.createElement("canvas");
            canvas.width = width;
            canvas.height = height;
            const ctx = canvas.getContext("2d");
            if (!ctx) return file;

            ctx.fillStyle = "#ffffff";
            ctx.fillRect(0, 0, width, height);
            ctx.drawImage(bitmap, 0, 0, width, height);
            closeImageBitmap(bitmap);

            const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, "image/jpeg", 0.88));
            if (!blob) return file;
            return new File([blob], replaceImageExtension(file.name || "profile-picture", "jpg"), { type: "image/jpeg" });
        } catch (_) {
            return file;
        }
    }

    async function prepareNotePhotoFile(file: File): Promise<File> {
        try {
            const bitmap = await loadProfileBitmap(file);
            const scale = Math.min(1, NOTE_PHOTO_MAX_DIMENSION / Math.max(bitmap.width, bitmap.height));
            const width = Math.max(1, Math.round(bitmap.width * scale));
            const height = Math.max(1, Math.round(bitmap.height * scale));
            const canvas = document.createElement("canvas");
            canvas.width = width;
            canvas.height = height;
            const ctx = canvas.getContext("2d");
            if (!ctx) return file;

            ctx.fillStyle = "#ffffff";
            ctx.fillRect(0, 0, width, height);
            ctx.drawImage(bitmap, 0, 0, width, height);
            closeImageBitmap(bitmap);

            let blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, "image/webp", 0.86));
            let extension = "webp";
            if (!blob || blob.type !== "image/webp") {
                blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, "image/jpeg", 0.88));
                extension = "jpg";
            }

            if (!blob) return file;
            const type = blob.type || (extension === "jpg" ? "image/jpeg" : "image/webp");
            return new File([blob], replaceImageExtension(file.name || "check-in-photo", extension), { type });
        } catch (_) {
            return file;
        }
    }

    async function loadProfileBitmap(file: File): Promise<ImageBitmap | HTMLImageElement> {
        if (typeof globalThis.createImageBitmap === "function") {
            try {
                return await createImageBitmap(file, { imageOrientation: "from-image" });
            } catch (_) {
            }
        }

        return await new Promise<HTMLImageElement>((resolve, reject) => {
            const url = URL.createObjectURL(file);
            const image = new Image();
            image.onload = () => {
                URL.revokeObjectURL(url);
                resolve(image);
            };
            image.onerror = () => {
                URL.revokeObjectURL(url);
                reject(new Error("Image preview failed"));
            };
            image.src = url;
        });
    }

    function closeImageBitmap(bitmap: ImageBitmap | HTMLImageElement): void {
        if ("close" in bitmap && typeof bitmap.close === "function") bitmap.close();
    }

    function replaceImageExtension(name: string, extension: string): string {
        const clean = String(name || "profile-picture").replace(/\.[^.]+$/, "");
        return `${clean || "profile-picture"}.${extension}`;
    }

    function renderNotes(notes: ParticipantNote[], participantView: boolean): void {
        const container = document.getElementById("lmxNotes");
        if (!container) return;
        if (!notes.length) {
            container.innerHTML = `<div class="lmx-note"><strong>${participantView ? "No participant notes yet." : "No public notes yet."}</strong></div>`;
            return;
        }

        container.innerHTML = notes.map(note => {
            const images = Array.isArray(note.images) ? note.images : [];
            const imageHtml = images.length
                ? `<div class="lmx-note-photo-grid">${images.map((image, index) => notePhotoHtml(image, `${note.participantId}-${note.challengeDay}-${index}`)).join("")}</div>`
                : "";
            const noteText = String(note.note || "").trim();
            return `<article class="lmx-note">
                <strong>${esc(note.displayName)} · Day ${note.challengeDay}</strong>
                ${noteText ? `<p>${esc(noteText)}</p>` : ""}
                ${imageHtml}
            </article>`;
        }).join("");
    }

    function parseCommitmentAmount(inputId: string): number {
        const input = optionalInput(inputId);
        if (input) sanitizeCommitmentAmountInput(input);
        const raw = String(input?.value || "").trim();
        const normalized = raw;
        const value = Number(normalized);
        if (!Number.isFinite(value) || value < 1) {
            const message = raw ? "Commitment amount must be at least USD 1." : "Enter a commitment amount of at least USD 1.";
            markCommitmentAmountInvalid(input, message, true);
            input?.focus();
            throw new Error(message);
        }

        clearCommitmentAmountValidity(input);
        return Math.round(value * 100) / 100;
    }

    function parseOptionalCommitmentAmount(inputId: string): number | null {
        const input = optionalInput(inputId);
        if (input) sanitizeCommitmentAmountInput(input);
        const raw = String(input?.value || "").trim();
        if (!raw) {
            clearCommitmentAmountValidity(input);
            return null;
        }

        return parseCommitmentAmount(inputId);
    }

    function wireCommitmentAmountValidation(inputId: string): void {
        const input = optionalInput(inputId);
        if (!input || input.dataset.commitmentValidationWired) return;
        input.dataset.commitmentValidationWired = "true";
        input.addEventListener("input", () => {
            sanitizeCommitmentAmountInput(input);
            clearCommitmentAmountValidity(input);
        });
        input.addEventListener("invalid", () => {
            const raw = String(input.value || "").trim();
            markCommitmentAmountInvalid(input, raw ? "Commitment amount must be at least USD 1." : "Enter a commitment amount of at least USD 1.");
        });
    }

    function sanitizeCommitmentAmountInput(input: HTMLInputElement): void {
        const original = String(input.value || "");
        let next = "";
        let hasDecimal = false;
        let decimals = 0;

        for (const char of original.replace(",", ".")) {
            if (char >= "0" && char <= "9") {
                if (hasDecimal) {
                    if (decimals >= 2) continue;
                    decimals += 1;
                }
                next += char;
            } else if (char === "." && !hasDecimal) {
                hasDecimal = true;
                next += char;
            }
        }

        if (next.startsWith(".")) next = `0${next}`;
        if (input.value !== next) input.value = next;
    }

    function markCommitmentAmountInvalid(input: HTMLInputElement | null, message: string, report = false): void {
        if (!input) return;
        input.setAttribute("aria-invalid", "true");
        if (typeof input.setCustomValidity === "function") {
            input.setCustomValidity(message);
            if (report) input.reportValidity?.();
        }
    }

    function clearCommitmentAmountValidity(input: HTMLInputElement | null): void {
        if (!input) return;
        input.removeAttribute("aria-invalid");
        if (typeof input.setCustomValidity === "function") input.setCustomValidity("");
    }

    function setCommitmentInputValue(inputId: string, value: number | null): void {
        const input = optionalInput(inputId);
        if (!input) return;
        input.value = value === null || value === undefined ? "" : String(value);
    }

    function formatUsd(value: unknown): string {
        const amount = Number(value);
        if (!Number.isFinite(amount)) return "USD -";
        return new Intl.NumberFormat("en-US", {
            style: "currency",
            currency: "USD",
            minimumFractionDigits: amount % 1 === 0 ? 0 : 2,
            maximumFractionDigits: 2
        }).format(amount);
    }

    function formatNumber(value: unknown): string {
        const number = Number(value);
        if (!Number.isFinite(number)) return "-";
        return new Intl.NumberFormat("en-US", { maximumFractionDigits: 2 }).format(number);
    }

    function participantNameHtml(row: LeaderboardRow, nameHtml: string, rank: number): string {
        const athlete = findAthleteForParticipant(row);
        const profileImage = String(row.profileImageUrl || "").trim();
        const athleteProfileImage = isPlaceholderProfileImage(athlete?.profilePic) ? "" : (athlete?.profilePic || "");
        const image = athleteProfileImage || profileImage || ATHLETE_PLACEHOLDER_IMAGE;
        const hasProfileImage = !!(athleteProfileImage || profileImage);
        const avatarClass = hasProfileImage ? "lmx-participant-avatar" : "lmx-participant-avatar placeholder";
        const alt = hasProfileImage ? `${row.displayName || "Participant"} profile picture` : "";
        const badges = [
            row.commitmentStatus === "commitment-due" ? "Commitment due" : "",
            row.challengeInactive ? "Resting" : ""
        ].filter(Boolean);
        const rankNumber = Number.isFinite(Number(rank)) ? Math.trunc(Number(rank)) : null;
        const rankHtml = rankNumber && rankNumber > 0
            ? `<span class="lmx-rank" aria-label="Rank ${rankNumber}">#${rankNumber}</span>`
            : "";
        return `<div class="lmx-participant-name">
            ${rankHtml}
            <span class="${avatarClass}" aria-hidden="${hasProfileImage ? "false" : "true"}">
                <img src="${escAttr(image)}" alt="${escAttr(alt)}" loading="lazy" decoding="async">
            </span>
            <span class="lmx-participant-label">${nameHtml}${badges.length ? `<span class="lmx-row-badges">${badges.map(badge => `<em>${esc(badge)}</em>`).join("")}</span>` : ""}</span>
        </div>`;
    }

    function isPlaceholderProfileImage(url: unknown): boolean {
        const value = String(url || "").trim();
        return !value || value.includes(ATHLETE_PLACEHOLDER_IMAGE);
    }

    function findAthleteForParticipant(row: LeaderboardRow): AthleteOption | null {
        if (!row || !row.athleteUrl || !athleteDirectory.length) return null;
        const slug = normalizeAthleteSlug(row.athleteUrl);
        if (!slug) return null;
        return athleteDirectory.find(athlete => normalizeAthleteSlug(athlete.slug) === slug) || null;
    }

    function isAthleteRecord(value: unknown): value is AthleteRecord {
        return typeof value === "object" && value !== null && !Array.isArray(value);
    }

    function loadAthleteDirectory(): Promise<AthleteOption[]> {
        if (athleteDirectoryPromise) return athleteDirectoryPromise;

        athleteDirectoryPromise = fetch("/api/data/athletes")
            .then(async response => {
                if (!response.ok) return [];
                const data: unknown = await response.json();
                return data;
            })
            .then(data => {
                const athletes = Array.isArray(data) ? data.filter(isAthleteRecord) : [];
                quoteAthleteResults = buildQuoteAthleteResults(athletes);
                athleteDirectory = athletes
                    .map(a => ({
                        name: String(a.DisplayName || a.Name || "").trim(),
                        legalName: String(a.Name || "").trim(),
                        slug: String(a.AthleteSlug || "").trim(),
                        profilePic: String(a.ProfilePicLeaderboardThumb || a.ProfilePicThumb || a.ProfilePic || "").trim()
                    }))
                    .filter(a => a.name && a.slug)
                    .sort((a, b) => a.name.localeCompare(b.name));

                if (publicState) renderAll();
                return athleteDirectory;
            })
            .catch(() => {
                athleteDirectoryPromise = null;
                return [];
            });

        return athleteDirectoryPromise;
    }

    function buildQuoteAthleteResults(athletes: AthleteRecord[]): QuoteAthlete[] {
        const rows = (Array.isArray(athletes) ? athletes : [])
            .map(buildQuoteAthleteRankRow)
            .filter((athlete): athlete is QuoteAthlete => athlete !== null)
            .sort(compareQuoteAthleteRank);

        rows.forEach((athlete, index) => {
            athlete.rank = index + 1;
        });
        assignQuoteBestRankCandidates(rows);
        return rows;
    }

    function buildQuoteAthleteRankRow(athlete: AthleteRecord): QuoteAthlete | null {
        const slug = String(athlete?.AthleteSlug || "").trim();
        const name = String(athlete?.DisplayName || athlete?.Name || "").trim();
        const legalName = String(athlete?.Name || "").trim();
        const dob = parseQuoteDateOfBirth(athlete?.DateOfBirth);
        if (!slug || !name || !dob) return null;

        const now = new Date();
        const chronologicalAge = calculateQuoteAgeAtDate(dob, now);
        const phenoStats = calculateQuotePhenoStats(athlete, dob, chronologicalAge);
        const bortzStats = calculateQuoteBortzStats(athlete, dob);
        const crowdAge = toFiniteNumber(athlete.CrowdAge);
        const crowdCount = Math.max(0, Math.trunc(toFiniteNumber(athlete.CrowdCount) || 0));
        const crowdAgeReduction = crowdAge !== null && Number.isFinite(chronologicalAge)
            ? crowdAge - chronologicalAge
            : null;
        const generationApi = readSharedWindowApi();
        const generation = typeof generationApi.getGeneration === "function"
            ? generationApi.getGeneration.call(window, dob.getFullYear())
            : resolveQuoteGeneration(dob.getFullYear());

        return {
            name,
            legalName,
            slug,
            profilePic: String(athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb || athlete.ProfilePic || "").trim(),
            dateOfBirth: dob,
            chronologicalAge,
            crowdAge,
            crowdCount,
            crowdAgeReduction,
            lowestBortzAge: bortzStats.lowestBortzAge,
            chronoAtLowestBortzAge: bortzStats.chronoAtLowestBortzAge,
            bortzAgeReduction: bortzStats.bortzAgeReduction,
            bortzAgeImprovement: Number.isFinite(athlete.BortzAgeImprovementFromWorst)
                ? Number(athlete.BortzAgeImprovementFromWorst)
                : bortzStats.bortzAgeImprovement,
            lowestPhenoAge: phenoStats.lowestPhenoAge,
            chronoAtLowestPhenoAge: phenoStats.chronoAtLowestPhenoAge,
            ageReduction: phenoStats.ageReduction,
            ageReductionPercent: phenoStats.ageReductionPercent,
            phenoAgeImprovement: Number.isFinite(athlete.PhenoAgeImprovementFromWorst)
                ? Number(athlete.PhenoAgeImprovementFromWorst)
                : phenoStats.phenoAgeImprovement,
            division: String(athlete.Division || "").trim(),
            generation,
            exclusiveLeague: String(athlete.ExclusiveLeague || "").trim(),
            podcastLink: String(athlete.PodcastLink || "").trim(),
            bestRankCandidates: []
        };
    }

    function calculateQuotePhenoStats(athlete: AthleteRecord, dob: Date, chronologicalAge: number): QuotePhenoStats {
        let lowestPhenoAge = Infinity;
        let chronoAtLowestPhenoAge = chronologicalAge;
        const phenoAgeApi = readSharedWindowApi().PhenoAge;
        const phenoSubmissionAges = quoteBiomarkers(athlete)
            .filter(isQuoteCompleteBiomarkerSet)
            .map((entry, index) => {
                const submittedAt = parseQuoteDate(entry.Date) || new Date();
                const ageAtEntry = calculateQuoteAgeAtDate(dob, submittedAt);
                const values = [
                    ageAtEntry,
                    entry.AlbGL,
                    entry.CreatUmolL,
                    entry.GluMmolL,
                    Math.log(entry.CrpMgL / 10),
                    entry.Wbc1000cellsuL,
                    entry.LymPc,
                    entry.McvFL,
                    entry.RdwPc,
                    entry.AlpUL
                ];
                const phenoAge = phenoAgeApi && typeof phenoAgeApi.calculatePhenoAge === "function"
                    ? phenoAgeApi.calculatePhenoAge(values)
                    : NaN;
                if (Number.isFinite(phenoAge) && phenoAge < lowestPhenoAge) {
                    lowestPhenoAge = phenoAge;
                    chronoAtLowestPhenoAge = ageAtEntry;
                }
                return { submittedAt, index, phenoAge };
            })
            .filter(result => Number.isFinite(result.phenoAge));

        if (!Number.isFinite(lowestPhenoAge)) {
            lowestPhenoAge = chronologicalAge;
            chronoAtLowestPhenoAge = chronologicalAge;
        }

        const ageReduction = Number.isFinite(lowestPhenoAge) && Number.isFinite(chronoAtLowestPhenoAge)
            ? lowestPhenoAge - chronoAtLowestPhenoAge
            : null;
        const ageReductionPercent = Number.isFinite(lowestPhenoAge) && Number.isFinite(chronoAtLowestPhenoAge) && chronoAtLowestPhenoAge > 0
            ? (1 - lowestPhenoAge / chronoAtLowestPhenoAge) * 100
            : null;
        let phenoAgeImprovement = null;
        if (phenoSubmissionAges.length >= 2) {
            const latest = phenoSubmissionAges.slice().sort(compareQuoteSubmissionDate).at(-1);
            const worst = Math.max(...phenoSubmissionAges.map(result => result.phenoAge));
            if (latest) phenoAgeImprovement = latest.phenoAge - worst;
        }

        return {
            lowestPhenoAge,
            chronoAtLowestPhenoAge,
            ageReduction,
            ageReductionPercent,
            phenoAgeImprovement
        };
    }

    function calculateQuoteBortzStats(athlete: AthleteRecord, dob: Date): QuoteBortzStats {
        let lowestBortzAge = null;
        let chronoAtLowestBortzAge = null;
        let bortzAgeReduction = null;
        let bortzAgeImprovement = null;
        let bortzMin = Infinity;
        const bortzAgeApi = readSharedWindowApi().BortzAge;

        const bortzSubmissionAges = quoteBiomarkers(athlete)
            .filter(isQuoteCompleteBortzBiomarkerSet)
            .map((entry, index) => {
                const submittedAt = parseQuoteDate(entry.Date) || new Date();
                const ageAtEntry = calculateQuoteAgeAtDate(dob, submittedAt);
                const values = buildQuoteBortzValues(entry, ageAtEntry);
                const bortzAge = bortzAgeApi && typeof bortzAgeApi.calculateBortzAge === "function"
                    ? bortzAgeApi.calculateBortzAge(ageAtEntry, values)
                    : NaN;
                if (Number.isFinite(bortzAge) && bortzAge < bortzMin) {
                    bortzMin = bortzAge;
                    chronoAtLowestBortzAge = ageAtEntry;
                }
                return { submittedAt, index, bortzAge };
            })
            .filter(result => Number.isFinite(result.bortzAge));

        if (Number.isFinite(bortzMin) && chronoAtLowestBortzAge !== null) {
            lowestBortzAge = bortzMin;
            bortzAgeReduction = lowestBortzAge - chronoAtLowestBortzAge;
        }

        if (bortzSubmissionAges.length >= 2) {
            const latest = bortzSubmissionAges.slice().sort(compareQuoteSubmissionDate).at(-1);
            const worst = Math.max(...bortzSubmissionAges.map(result => result.bortzAge));
            if (latest) bortzAgeImprovement = latest.bortzAge - worst;
        }

        return {
            lowestBortzAge,
            chronoAtLowestBortzAge,
            bortzAgeReduction,
            bortzAgeImprovement
        };
    }

    function quoteBiomarkers(athlete: AthleteRecord): BiomarkerEntry[] {
        return Array.isArray(athlete?.Biomarkers) ? athlete.Biomarkers : [];
    }

    function isQuoteCompleteBiomarkerSet(entry: BiomarkerEntry): entry is CompletePhenoBiomarkerEntry {
        const values = [
            entry?.Wbc1000cellsuL,
            entry?.LymPc,
            entry?.McvFL,
            entry?.RdwPc,
            entry?.AlbGL,
            entry?.AlpUL,
            entry?.CreatUmolL,
            entry?.GluMmolL,
            entry?.CrpMgL
        ];
        return values.every(Number.isFinite) && Number(entry.CrpMgL) > 0;
    }

    function isQuoteCompleteBortzBiomarkerSet(entry: BiomarkerEntry): entry is CompleteBortzBiomarkerEntry {
        if (!entry || !entry.Date) return false;
        const values = [
            entry.AlbGL,
            entry.AlpUL,
            entry.UreaMmolL,
            entry.CholesterolMmolL,
            entry.CreatUmolL,
            entry.CystatinCMgL,
            entry.Hba1cMmolMol,
            entry.CrpMgL,
            entry.GgtUL,
            entry.Rbc10e12L,
            entry.McvFL,
            entry.RdwPc,
            entry.Wbc1000cellsuL,
            entry.MonocytePc,
            entry.NeutrophilPc,
            entry.LymPc,
            entry.AltUL,
            entry.ShbgNmolL,
            entry.VitaminDNmolL,
            entry.GluMmolL,
            entry.MchPg,
            entry.ApoA1GL
        ];
        return values.every(Number.isFinite) &&
            Number(entry.CrpMgL) > 0 &&
            Number(entry.GgtUL) > 0 &&
            Number(entry.AltUL) > 0 &&
            Number(entry.ShbgNmolL) > 0 &&
            Number(entry.VitaminDNmolL) > 0;
    }

    function buildQuoteBortzValues(entry: CompleteBortzBiomarkerEntry, ageAtEntry: number): number[] {
        const wbc = Number(entry.Wbc1000cellsuL);
        const monocyteCount = wbc * Number(entry.MonocytePc) / 100;
        const neutrophilCount = wbc * Number(entry.NeutrophilPc) / 100;
        return [
            ageAtEntry,
            entry.AlbGL,
            entry.AlpUL,
            entry.UreaMmolL,
            entry.CholesterolMmolL,
            entry.CreatUmolL,
            entry.CystatinCMgL,
            entry.Hba1cMmolMol,
            entry.CrpMgL,
            entry.GgtUL,
            entry.Rbc10e12L,
            entry.McvFL,
            entry.RdwPc,
            monocyteCount,
            neutrophilCount,
            entry.LymPc,
            entry.AltUL,
            entry.ShbgNmolL,
            entry.VitaminDNmolL,
            entry.GluMmolL,
            entry.MchPg,
            entry.ApoA1GL
        ];
    }

    function buildQuoteViewHref(view: string): string {
        return `/league/${encodeURIComponent(String(view || "").trim())}`;
    }

    function buildQuoteFiltersHref(filters: string | string[]): string {
        const values = (Array.isArray(filters) ? filters : [filters])
            .map(value => String(value || "").trim())
            .filter(Boolean)
            .map(value => quoteSlugifyName(value, true));
        return values.length ? `/league/${values.join("/")}` : "/leaderboard";
    }

    function quoteSlugifyName(name: string, encode: boolean): string {
        const slugifyName = readSharedWindowApi().slugifyName;
        if (typeof slugifyName === "function") {
            return slugifyName.call(window, name, encode);
        }

        const normalized = String(name || "")
            .trim()
            .toLowerCase()
            .normalize("NFKD")
            .replace(/[\u0300-\u036f]/g, "")
            .replace(/\s+/g, "-")
            .replace(/[^a-z0-9-]/g, "")
            .replace(/-+/g, "-")
            .replace(/^-|-$/g, "");
        return encode ? encodeURIComponent(normalized) : decodeURIComponent(normalized);
    }

    function assignQuoteBestRankCandidates(athletes: QuoteAthlete[]): void {
        const rows = Array.isArray(athletes) ? athletes : [];
        rows.forEach(athlete => {
            athlete.bestRankCandidates = [];
            if (isFiniteNumber(athlete.rank)) {
                addQuoteBestRankCandidate(athlete, {
                    rank: athlete.rank,
                    leagueName: "Ultimate League",
                    leagueLabel: "Ultimate League",
                    leagueType: "ultimate",
                    href: "/leaderboard",
                    targetBlank: true,
                    tiePriority: 0
                });
            }
        });

        assignQuoteRankedBestCandidates(
            sortedQuoteRankableAthletes(rows, hasQuoteBortzRankData, compareQuoteAthleteRank),
            () => ({
                leagueName: "Bortz Age",
                leagueLabel: "Bortz Age leaderboard",
                leagueType: "bortz",
                href: buildQuoteViewHref("bortz"),
                tiePriority: 10
            })
        );

        assignQuoteRankedBestCandidates(
            sortedQuoteRankableAthletes(rows, athlete => Number.isFinite(athlete.ageReduction), compareQuoteAthleteRankPhenoOnly),
            () => ({
                leagueName: "Pheno Age",
                leagueLabel: "Pheno Age leaderboard",
                leagueType: "pheno",
                href: buildQuoteViewHref("pheno"),
                tiePriority: 11
            })
        );

        assignQuoteRankedBestCandidates(
            sortedQuoteRankableAthletes(rows, athlete => !hasQuoteBortzRankData(athlete), compareQuoteAthleteRankPhenoOnly),
            () => ({
                leagueName: "Amateur League",
                leagueLabel: "Amateur League",
                leagueType: "amateur",
                href: buildQuoteFiltersHref("Amateur"),
                targetBlank: true,
                tiePriority: -1
            })
        );

        assignQuoteRankedBestCandidates(
            sortedQuoteRankableAthletes(rows, athlete => Number.isFinite(athlete.phenoAgeImprovement), compareQuoteAthleteRankPhenoImprovement),
            () => ({
                leagueName: "Pheno Improvement",
                leagueLabel: "Pheno Improvement leaderboard",
                leagueType: "pheno-improvement",
                href: buildQuoteViewHref("improvement"),
                tiePriority: 12
            })
        );

        assignQuoteRankedBestCandidates(
            sortedQuoteRankableAthletes(rows, athlete => Number.isFinite(athlete.bortzAgeImprovement), compareQuoteAthleteRankBortzImprovement),
            () => ({
                leagueName: "Bortz Improvement",
                leagueLabel: "Bortz Improvement leaderboard",
                leagueType: "bortz-improvement",
                href: buildQuoteViewHref("bortz-improvement"),
                tiePriority: 12
            })
        );

        assignQuoteRankedBestCandidates(
            sortedQuoteRankableAthletes(
                rows,
                athlete => athlete.crowdCount >= CROWD_AGE_LEADERBOARD_MINIMUM_GUESS_COUNT && Number.isFinite(athlete.crowdAgeReduction),
                compareQuoteAthleteRankCrowdAge),
            () => ({
                leagueName: "Crowd Age",
                leagueLabel: "Crowd Age leaderboard",
                leagueType: "crowd",
                href: buildQuoteViewHref("crowd"),
                tiePriority: 13
            })
        );

        assignQuoteRankedBestCandidates(
            orderByQuoteNumberDesc(
                rows.filter(athlete => Number.isFinite(athlete.ageReductionPercent)),
                athlete => athlete.ageReductionPercent),
            () => ({
                leagueName: "Pheno pace of aging",
                leagueLabel: "Pheno pace of aging ranking",
                leagueType: "pheno-pace",
                tiePriority: 14
            })
        );

        assignQuoteRankedBestCandidates(
            orderByQuoteNumberAsc(
                rows.filter(athlete => quoteBortzPace(athlete) !== null),
                quoteBortzPace),
            () => ({
                leagueName: "Bortz pace of aging",
                leagueLabel: "Bortz pace of aging ranking",
                leagueType: "bortz-pace",
                tiePriority: 14
            })
        );

        const addGroupedRanks = <T extends string>(
            values: T[],
            predicate: (athlete: QuoteAthlete, value: T) => boolean,
            comparator: (a: QuoteAthlete, b: QuoteAthlete) => number,
            candidateFactory: (athlete: QuoteAthlete, value: T) => QuoteRankCandidateInput
        ): void => {
            values.filter(Boolean).forEach(value => {
                assignQuoteRankedBestCandidates(
                    sortedQuoteRankableAthletes(rows, athlete => predicate(athlete, value), comparator),
                    athlete => candidateFactory(athlete, value)
                );
            });
        };

        const divisions = [...new Set(rows.map(athlete => athlete.division).filter(Boolean))];
        addGroupedRanks(
            divisions,
            (athlete, division) => athlete.division === division,
            compareQuoteAthleteRank,
            (_athlete, division) => ({
                leagueName: division,
                leagueLabel: `${division} League`,
                leagueType: "division",
                href: `/league/${quoteSlugifyName(division, true)}`,
                tiePriority: 20
            })
        );

        const generations = [...new Set(rows.map(athlete => athlete.generation).filter(Boolean))];
        addGroupedRanks(
            generations,
            (athlete, generation) => athlete.generation === generation,
            compareQuoteAthleteRank,
            (_athlete, generation) => ({
                leagueName: generation,
                leagueLabel: `${generation} League`,
                leagueType: "generation",
                href: `/league/${quoteSlugifyName(generation, true)}`,
                tiePriority: 21
            })
        );

        divisions.forEach(division => {
            generations.forEach(generation => {
                assignQuoteRankedBestCandidates(
                    sortedQuoteRankableAthletes(
                        rows,
                        athlete => athlete.division === division && athlete.generation === generation,
                        compareQuoteAthleteRank),
                    () => ({
                        leagueName: `${generation}_${division}`,
                        leagueLabel: `${generation} ${division} League`,
                        leagueType: "combination",
                        href: buildQuoteFiltersHref([generation, division]),
                        tiePriority: 22
                    })
                );
            });
        });

        const exclusiveLeagues = [...new Set(rows.map(athlete => athlete.exclusiveLeague).filter(Boolean))];
        addGroupedRanks(
            exclusiveLeagues,
            (athlete, exclusiveLeague) => athlete.exclusiveLeague === exclusiveLeague,
            compareQuoteAthleteRank,
            (_athlete, exclusiveLeague) => ({
                leagueName: exclusiveLeague,
                leagueLabel: `${exclusiveLeague} League`,
                leagueType: "exclusive",
                href: exclusiveLeague === "Prosperan"
                    ? "/league/prosperan"
                    : buildQuoteFiltersHref(exclusiveLeague),
                tiePriority: 23
            })
        );
    }

    function addQuoteBestRankCandidate(athlete: QuoteAthlete, candidate: QuoteRankCandidateInput): void {
        if (!athlete || !candidate) return;

        const rank = Number(candidate.rank);
        if (!Number.isFinite(rank) || rank < 1) return;

        const normalized: Omit<QuoteRankCandidate, "key"> = {
            rank,
            leagueName: String(candidate.leagueName || candidate.leagueLabel || "").trim(),
            leagueLabel: String(candidate.leagueLabel || candidate.leagueName || "").trim(),
            leagueLabelHtml: candidate.leagueLabelHtml || null,
            leagueType: String(candidate.leagueType || "other").trim(),
            href: candidate.href || null,
            targetBlank: !!candidate.targetBlank,
            tiePriority: isFiniteNumber(candidate.tiePriority) ? candidate.tiePriority : 100
        };
        if (!normalized.leagueName && !normalized.leagueLabel) return;

        if (!Array.isArray(athlete.bestRankCandidates)) {
            athlete.bestRankCandidates = [];
        }

        const key = `${normalized.leagueType}|${normalized.leagueName || normalized.leagueLabel}`;
        const existingIndex = athlete.bestRankCandidates.findIndex(item => item.key === key);
        if (existingIndex >= 0) {
            const existing = athlete.bestRankCandidates[existingIndex];
            if (existing && compareQuoteBestRankCandidates(normalized, existing) < 0) {
                athlete.bestRankCandidates[existingIndex] = { ...normalized, key };
            }
            return;
        }

        athlete.bestRankCandidates.push({ ...normalized, key });
    }

    function compareQuoteBestRankCandidates(a: Omit<QuoteRankCandidate, "key"> | QuoteRankCandidate, b: Omit<QuoteRankCandidate, "key"> | QuoteRankCandidate): number {
        if (!a) return 1;
        if (!b) return -1;

        if (a.rank !== b.rank) return a.rank - b.rank;
        if (a.tiePriority !== b.tiePriority) return a.tiePriority - b.tiePriority;
        return String(a.leagueLabel || a.leagueName || "").localeCompare(String(b.leagueLabel || b.leagueName || ""));
    }

    function computeQuoteAthleteBestRank(athlete: QuoteAthlete | AthleteOption | null): QuoteRankCandidateInput | null {
        if (!athlete || !("bestRankCandidates" in athlete)) return null;
        const candidates = Array.isArray(athlete.bestRankCandidates) ? athlete.bestRankCandidates : [];
        if (!candidates.length && isFiniteNumber(athlete.rank)) {
            return {
                rank: athlete.rank,
                leagueName: "Ultimate League",
                leagueLabel: "Ultimate League",
                leagueType: "ultimate",
                href: "/leaderboard",
                targetBlank: true,
                tiePriority: 0
            };
        }
        return candidates.slice().sort(compareQuoteBestRankCandidates)[0] || null;
    }

    function assignQuoteRankedBestCandidates(
        items: QuoteAthlete[],
        candidateFactory: (athlete: QuoteAthlete, index: number) => QuoteRankCandidateInput
    ): void {
        (Array.isArray(items) ? items : []).forEach((athlete, index) => {
            addQuoteBestRankCandidate(athlete, {
                ...candidateFactory(athlete, index),
                rank: index + 1
            });
        });
    }

    function sortedQuoteRankableAthletes(
        athletes: QuoteAthlete[],
        predicate: (athlete: QuoteAthlete) => boolean,
        comparator: (a: QuoteAthlete, b: QuoteAthlete) => number
    ): QuoteAthlete[] {
        return (Array.isArray(athletes) ? athletes : [])
            .filter(predicate || (() => true))
            .slice()
            .sort(comparator);
    }

    function hasQuoteBortzRankData(athlete: QuoteAthlete): boolean {
        return athlete && athlete.bortzAgeReduction != null && Number.isFinite(athlete.bortzAgeReduction);
    }

    function quoteBortzPace(athlete: QuoteAthlete): number | null {
        const lowestAge = athlete.lowestBortzAge;
        const chronologicalAge = athlete.chronoAtLowestBortzAge;
        if (lowestAge === null || chronologicalAge === null) return null;
        const pace = lowestAge / chronologicalAge;
        return Number.isFinite(pace) ? pace : null;
    }

    function compareQuoteAthleteRank(a: QuoteAthlete, b: QuoteAthlete): number {
        const aHasBortz = hasQuoteBortzRankData(a);
        const bHasBortz = hasQuoteBortzRankData(b);
        if (aHasBortz && !bHasBortz) return -1;
        if (!aHasBortz && bHasBortz) return 1;

        const aRed = (aHasBortz ? a.bortzAgeReduction : a.ageReduction) ?? 0;
        const bRed = (bHasBortz ? b.bortzAgeReduction : b.ageReduction) ?? 0;
        if (aRed < bRed) return -1;
        if (aRed > bRed) return 1;

        return compareQuoteDobAndName(a, b);
    }

    function compareQuoteAthleteRankPhenoOnly(a: QuoteAthlete, b: QuoteAthlete): number {
        const aRed = isFiniteNumber(a.ageReduction) ? a.ageReduction : 0;
        const bRed = isFiniteNumber(b.ageReduction) ? b.ageReduction : 0;
        if (aRed < bRed) return -1;
        if (aRed > bRed) return 1;
        return compareQuoteDobAndName(a, b);
    }

    function compareQuoteAthleteRankPhenoImprovement(a: QuoteAthlete, b: QuoteAthlete): number {
        const aImprovement = isFiniteNumber(a.phenoAgeImprovement) ? a.phenoAgeImprovement : Infinity;
        const bImprovement = isFiniteNumber(b.phenoAgeImprovement) ? b.phenoAgeImprovement : Infinity;
        if (aImprovement < bImprovement) return -1;
        if (aImprovement > bImprovement) return 1;
        return compareQuoteAthleteRankPhenoOnly(a, b);
    }

    function compareQuoteAthleteRankBortzImprovement(a: QuoteAthlete, b: QuoteAthlete): number {
        const aImprovement = isFiniteNumber(a.bortzAgeImprovement) ? a.bortzAgeImprovement : Infinity;
        const bImprovement = isFiniteNumber(b.bortzAgeImprovement) ? b.bortzAgeImprovement : Infinity;
        if (aImprovement < bImprovement) return -1;
        if (aImprovement > bImprovement) return 1;
        return compareQuoteAthleteRank(a, b);
    }

    function compareQuoteAthleteRankCrowdAge(a: QuoteAthlete, b: QuoteAthlete): number {
        const aReduction = isFiniteNumber(a.crowdAgeReduction) ? a.crowdAgeReduction : -Infinity;
        const bReduction = isFiniteNumber(b.crowdAgeReduction) ? b.crowdAgeReduction : -Infinity;
        if (aReduction < bReduction) return -1;
        if (aReduction > bReduction) return 1;

        const aCount = Number.isFinite(a.crowdCount) ? a.crowdCount : 0;
        const bCount = Number.isFinite(b.crowdCount) ? b.crowdCount : 0;
        if (aCount > bCount) return -1;
        if (aCount < bCount) return 1;

        return compareQuoteDobAndName(a, b);
    }

    function compareQuoteDobAndName(a: QuoteAthlete, b: QuoteAthlete): number {
        if (a.dateOfBirth < b.dateOfBirth) return -1;
        if (a.dateOfBirth > b.dateOfBirth) return 1;
        if (a.name < b.name) return -1;
        if (a.name > b.name) return 1;
        return 0;
    }

    function compareQuoteSubmissionDate(a: QuoteSubmissionAge, b: QuoteSubmissionAge): number {
        const timeDiff = a.submittedAt.getTime() - b.submittedAt.getTime();
        return timeDiff !== 0 ? timeDiff : a.index - b.index;
    }

    function orderByQuoteNumberDesc<T>(items: T[], selector: (item: T) => number | null | undefined): T[] {
        return [...items].sort((a, b) => {
            const aValue = selector(a);
            const bValue = selector(b);
            const aRank = isFiniteNumber(aValue) ? aValue : Number.NEGATIVE_INFINITY;
            const bRank = isFiniteNumber(bValue) ? bValue : Number.NEGATIVE_INFINITY;
            return bRank - aRank;
        });
    }

    function orderByQuoteNumberAsc<T>(items: T[], selector: (item: T) => number | null | undefined): T[] {
        return [...items].sort((a, b) => {
            const aValue = selector(a);
            const bValue = selector(b);
            const aRank = isFiniteNumber(aValue) ? aValue : Number.POSITIVE_INFINITY;
            const bRank = isFiniteNumber(bValue) ? bValue : Number.POSITIVE_INFINITY;
            return aRank - bRank;
        });
    }

    function parseQuoteDateOfBirth(dateOfBirth: DateOfBirthParts | undefined): Date | null {
        const year = Number(dateOfBirth?.Year);
        const month = Number(dateOfBirth?.Month);
        const day = Number(dateOfBirth?.Day);
        if (!Number.isFinite(year) || !Number.isFinite(month) || !Number.isFinite(day)) return null;
        const date = new Date(year, month - 1, day);
        return Number.isNaN(date.getTime()) ? null : date;
    }

    function parseQuoteDate(value: string): Date | null {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? null : date;
    }

    function calculateQuoteAgeAtDate(dob: Date, date: Date): number {
        const calculateAgeAtDate = readSharedWindowApi().calculateAgeAtDate;
        if (typeof calculateAgeAtDate === "function") {
            try {
                return calculateAgeAtDate.call(window, dob, date);
            } catch (_) {
            }
        }

        const msPerDay = 1000 * 60 * 60 * 24;
        const start = Date.UTC(dob.getFullYear(), dob.getMonth(), dob.getDate());
        const end = Date.UTC(date.getFullYear(), date.getMonth(), date.getDate());
        return Math.round(((end - start) / msPerDay / 365.2425) * 100) / 100;
    }

    function resolveQuoteGeneration(birthYear: number): string {
        if (birthYear >= 1928 && birthYear <= 1945) return "Silent Generation";
        if (birthYear >= 1946 && birthYear <= 1964) return "Baby Boomers";
        if (birthYear >= 1965 && birthYear <= 1980) return "Gen X";
        if (birthYear >= 1981 && birthYear <= 1996) return "Millennials";
        if (birthYear >= 1997 && birthYear <= 2012) return "Gen Z";
        if (birthYear >= 2013) return "Gen Alpha";
        return "Unknown";
    }

    function toFiniteNumber(value: unknown): number | null {
        const number = Number(value);
        return Number.isFinite(number) ? number : null;
    }

    function isFiniteNumber(value: unknown): value is number {
        return typeof value === "number" && Number.isFinite(value);
    }

    function initAthleteSelectors() {
        const inputs = ["lmxSignupAthlete"]
            .map(optionalInput)
            .filter((input): input is HTMLInputElement => input !== null);
        if (!inputs.length) return;

        inputs.forEach(input => {
            input.setAttribute("role", "combobox");
            input.setAttribute("aria-autocomplete", "list");
            input.setAttribute("aria-expanded", "false");
            input.setAttribute("aria-haspopup", "listbox");
        });

        loadAthleteDirectory()
            .then(athletes => inputs.forEach(input => wireAthleteSelector(input, athletes)));
    }

    function wireAthleteSelector(input: HTMLInputElement, athletes: AthleteOption[]): void {
        if (athleteSelectors.has(input.id)) return;

        let currentFocus = -1;
        const selected = document.getElementById(`${input.id}Selected`);
        const clearButton = document.getElementById(`${input.id}Clear`);
        const selector: AthleteSelectorController = {
            input,
            athletes,
            setValue(value: string) {
                const raw = String(value || "").trim();
                const normalized = normalizeAthleteSlug(raw);
                const match = athletes.find(a =>
                    normalizeAthleteSlug(a.slug) === normalized ||
                    a.name.toLowerCase() === raw.toLowerCase() ||
                    a.legalName.toLowerCase() === raw.toLowerCase());
                if (match) {
                    select(match);
                    return;
                }

                input.value = raw;
                clearSelection();
                updateSelectedState(null);
            },
            clear() {
                input.value = "";
                clearSelection();
                updateSelectedState(null);
                closeList();
            },
            getPayload() {
                const raw = input.value.trim();
                if (!raw) {
                    input.setCustomValidity?.("");
                    return null;
                }

                const selectedName = input.dataset.athleteName || "";
                if (input.dataset.athleteSlug && raw.toLowerCase() === selectedName.toLowerCase()) {
                    input.setCustomValidity?.("");
                    return input.dataset.athleteSlug;
                }

                const normalized = normalizeAthleteSlug(raw);
                const match = athletes.find(a =>
                    a.name.toLowerCase() === raw.toLowerCase() ||
                    a.legalName.toLowerCase() === raw.toLowerCase() ||
                    normalizeAthleteSlug(a.slug) === normalized);
                if (match) {
                    select(match);
                    input.setCustomValidity?.("");
                    return match.slug;
                }

                const message = "Select an athlete from the list or clear this field.";
                input.setCustomValidity?.(message);
                input.reportValidity?.();
                input.focus();
                throw new Error(message);
            },
            getSelectedName(): string {
                return input.dataset.athleteName || input.value.trim();
            }
        };

        athleteSelectors.set(input.id, selector);
        selector.setValue(input.value);

        input.addEventListener("input", () => {
            input.setCustomValidity?.("");
            clearSelection();
            updateSelectedState(null);
            renderSuggestions();
        });

        input.addEventListener("focus", () => {
            if (!input.value.trim()) renderSuggestions(true);
        });

        input.addEventListener("keydown", event => {
            let list = document.getElementById(`${input.id}-autocomplete-list`);
            const items = list ? Array.from(list.getElementsByClassName("lmx-athlete-option")) : [];

            if (event.key === "ArrowDown") {
                event.preventDefault();
                currentFocus++;
                setActive(items);
            } else if (event.key === "ArrowUp") {
                event.preventDefault();
                currentFocus--;
                setActive(items);
            } else if (event.key === "Enter" && currentFocus > -1) {
                const activeItem = items[currentFocus];
                if (!activeItem) return;
                event.preventDefault();
                activeItem.dispatchEvent(new MouseEvent("mousedown"));
            } else if (event.key === "Escape") {
                closeList();
            }
        });

        document.addEventListener("click", event => {
            if (!(event.target instanceof Node) || !input.closest(".lmx-athlete-selector")?.contains(event.target)) closeList();
        });

        clearButton?.addEventListener("click", () => {
            selector.clear();
            input.focus();
        });

        function renderSuggestions(showInitial = false): void {
            const query = input.value.trim().toLowerCase();
            const terms = query.split(/\s+/).filter(Boolean);
            closeList();
            if (!terms.length && !showInitial) return;

            const matches = athletes
                .filter(a => {
                    if (!terms.length) return true;
                    const name = a.name.toLowerCase();
                    const legalName = a.legalName.toLowerCase();
                    const slug = normalizeAthleteSlug(a.slug);
                    return terms.every(term => name.includes(term) || legalName.includes(term) || slug.includes(term));
                })
                .slice(0, 6);

            const list = document.createElement("div");
            list.id = `${input.id}-autocomplete-list`;
            list.className = "lmx-athlete-options";
            list.setAttribute("role", "listbox");
            input.closest(".lmx-athlete-picker")?.appendChild(list);
            input.setAttribute("aria-expanded", "true");

            if (!matches.length) {
                list.innerHTML = `<div class="lmx-athlete-empty" role="option" aria-disabled="true">No listed athlete found</div>`;
                return;
            }

            matches.forEach(athlete => {
                const item = document.createElement("div");
                item.className = "lmx-athlete-option";
                item.setAttribute("role", "option");
                item.innerHTML = `
                    ${athlete.profilePic ? `<img src="${escAttr(athlete.profilePic)}" alt="" loading="lazy">` : "<span class=\"lmx-athlete-fallback\"></span>"}
                    <span><span class="lmx-athlete-name">${highlightMatch(athlete.name, terms[0] || "")}</span><em>${esc(athlete.slug.replace(/_/g, "-"))}</em></span>`;
                item.addEventListener("mousedown", event => {
                    event.preventDefault();
                    select(athlete);
                    closeList();
                });
                list.appendChild(item);
            });
        }

        function select(athlete: AthleteOption): void {
            input.value = athlete.name;
            input.dataset.athleteSlug = athlete.slug;
            input.dataset.athleteName = athlete.name;
            input.setCustomValidity?.("");
            updateSelectedState(athlete);
        }

        function clearSelection(): void {
            delete input.dataset.athleteSlug;
            delete input.dataset.athleteName;
        }

        function updateSelectedState(athlete: AthleteOption | null): void {
            const hasSelection = !!athlete;
            input.classList.toggle("has-athlete-selection", hasSelection);
            clearButton?.classList.toggle("lmx-hidden", !hasSelection && !input.value.trim());

            if (!selected) return;
            selected.classList.toggle("lmx-hidden", !hasSelection);
            selected.innerHTML = hasSelection
                ? `${athlete.profilePic ? `<img src="${escAttr(athlete.profilePic)}" alt="" loading="lazy">` : "<span class=\"lmx-athlete-fallback\"></span>"}
                   <span><strong>${esc(athlete.name)}</strong><em>${esc(athlete.slug.replace(/_/g, "-"))}</em></span>`
                : "";
        }

        function closeList(): void {
            document.getElementById(`${input.id}-autocomplete-list`)?.remove();
            input.setAttribute("aria-expanded", "false");
            currentFocus = -1;
        }

        function setActive(items: Element[]): void {
            if (!items.length) return;
            items.forEach(item => item.classList.remove("autocomplete-active"));
            if (currentFocus >= items.length) currentFocus = 0;
            if (currentFocus < 0) currentFocus = items.length - 1;
            const activeItem = items[currentFocus];
            activeItem?.classList.add("autocomplete-active");
            activeItem?.scrollIntoView({ block: "nearest" });
        }
    }

    function getAthleteSelectorPayload(id: string): string | null {
        const selector = athleteSelectors.get(id);
        if (selector) return selector.getPayload();

        const input = optionalInput(id);
        if (!input) return null;

        const raw = input.value.trim();
        if (!raw) return null;

        const message = "Select an athlete from the list or clear this field.";
        input.setCustomValidity?.(message);
        input.reportValidity?.();
        input.focus();
        throw new Error(message);
    }

    function getRequiredAthleteSelectorPayload(id: string): string {
        const input = optionalInput(id);
        const payload = getAthleteSelectorPayload(id);
        if (payload) return payload;

        const message = "Select your athlete profile.";
        input?.setCustomValidity?.(message);
        input?.reportValidity?.();
        input?.focus();
        throw new Error(message);
    }

    function getAthleteSelectorDisplayName(id: string): string {
        const selector = athleteSelectors.get(id);
        if (selector) return selector.getSelectedName();

        const input = optionalInput(id);
        return input?.dataset.athleteName || input?.value.trim() || "";
    }

    function setAthleteSelectorValue(id: string, value: string): void {
        const selector = athleteSelectors.get(id);
        if (selector) {
            selector.setValue(value);
            return;
        }

        const input = optionalInput(id);
        if (input) input.value = value || "";
    }

    function clearAthleteSelector(id: string): void {
        const selector = athleteSelectors.get(id);
        if (selector) {
            selector.clear();
            return;
        }

        const input = optionalInput(id);
        if (!input) return;
        input.value = "";
        delete input.dataset.athleteSlug;
        delete input.dataset.athleteName;
        input.setCustomValidity?.("");
    }

    function normalizeAthleteSlug(value: unknown): string {
        let raw = String(value || "").trim();
        if (!raw) return "";

        try {
            raw = new URL(raw, window.location.origin).pathname;
        } catch (_) {}

        raw = raw.replace(/^\/+|\/+$/g, "");
        if (raw.toLowerCase().startsWith("athlete/")) raw = raw.slice("athlete/".length);
        return raw
            .trim()
            .replace(/_/g, "-")
            .toLowerCase()
            .replace(/[^a-z0-9-]/g, "-")
            .replace(/-+/g, "-")
            .replace(/^-|-$/g, "");
    }

    function highlightMatch(value: string, term: string): string {
        const text = String(value || "");
        const lower = text.toLowerCase();
        const needle = String(term || "").toLowerCase();
        const index = needle ? lower.indexOf(needle) : -1;
        if (index < 0) return esc(text);

        return `${esc(text.slice(0, index))}<strong>${esc(text.slice(index, index + needle.length))}</strong>${esc(text.slice(index + needle.length))}`;
    }

    function getPendingCheckInDays(state: ParticipantState): EligibleDay[] {
        return ((state && state.eligibleDays) || []).filter(day => !day.existing);
    }

    type Properties<K extends string> = { [P in K]: unknown };

    function hasProperties<K extends string>(value: unknown, ...keys: K[]): value is object & Properties<K> {
        return typeof value === "object" && value !== null && keys.every(key => key in value);
    }

    function isNullableString(value: unknown): value is string | null {
        return value === null || typeof value === "string";
    }

    function isNullableNumber(value: unknown): value is number | null {
        return value === null || typeof value === "number";
    }

    function isArrayOf<T>(value: unknown, guard: (item: unknown) => item is T): value is T[] {
        return Array.isArray(value) && value.every(guard);
    }

    function isDaySummary(value: unknown): value is DaySummary {
        return hasProperties(value, "challengeDay", "date") &&
            typeof value.challengeDay === "number" && typeof value.date === "string";
    }

    function isDayCell(value: unknown): value is DayCell {
        return hasProperties(value, "challengeDay", "checkedIn", "score", "countsForScore", "sleep", "exercise", "nutrition", "vices") &&
            typeof value.challengeDay === "number" && typeof value.checkedIn === "boolean" &&
            isNullableNumber(value.score) && typeof value.countsForScore === "boolean" &&
            isNullableNumber(value.sleep) && isNullableNumber(value.exercise) &&
            isNullableNumber(value.nutrition) && isNullableNumber(value.vices);
    }

    function isCheckInImage(value: unknown): value is CheckInImage {
        return hasProperties(value, "url", "width", "height") &&
            typeof value.url === "string" && typeof value.width === "number" && typeof value.height === "number";
    }

    function isCheckInDraft(value: unknown): value is CheckInDraft {
        return hasProperties(value, "sleep", "exercise", "nutrition", "vices", "note", "images") &&
            typeof value.sleep === "number" && typeof value.exercise === "number" &&
            typeof value.nutrition === "number" && typeof value.vices === "number" &&
            isNullableString(value.note) && isArrayOf(value.images, isCheckInImage);
    }

    function isEligibleDay(value: unknown): value is EligibleDay {
        return hasProperties(value, "challengeDay", "date", "countsForScore", "existing") &&
            typeof value.challengeDay === "number" && typeof value.date === "string" &&
            typeof value.countsForScore === "boolean" && (value.existing === null || isCheckInDraft(value.existing));
    }

    function isParticipantNote(value: unknown): value is ParticipantNote {
        return hasProperties(value, "participantId", "displayName", "challengeDay", "date", "note", "updatedAtUtc", "images") &&
            typeof value.participantId === "string" && typeof value.displayName === "string" &&
            typeof value.challengeDay === "number" && typeof value.date === "string" &&
            isNullableString(value.note) && typeof value.updatedAtUtc === "string" &&
            isArrayOf(value.images, isCheckInImage);
    }

    function isLeaderboardRow(value: unknown): value is LeaderboardRow {
        return hasProperties(value, "participantId", "displayName", "athleteUrl", "profileImageUrl", "checkedInDays", "totalPoints", "currentStreak", "cells", "badges", "latestCheckInAtUtc", "challengeEmailsStopped", "challengeInactive", "commitmentStatus") &&
            typeof value.participantId === "string" && typeof value.displayName === "string" &&
            isNullableString(value.athleteUrl) && isNullableString(value.profileImageUrl) &&
            typeof value.checkedInDays === "number" && typeof value.totalPoints === "number" &&
            typeof value.currentStreak === "number" && isArrayOf(value.cells, isDayCell) &&
            Array.isArray(value.badges) && value.badges.every(badge => typeof badge === "string") &&
            isNullableString(value.latestCheckInAtUtc) && typeof value.challengeEmailsStopped === "boolean" &&
            typeof value.challengeInactive === "boolean" && isNullableString(value.commitmentStatus);
    }

    function isPodiumRow(value: unknown): value is PodiumRow {
        return hasProperties(value, "placement", "displayName", "athleteUrl", "profileImageUrl", "checkedInDays", "totalPoints") &&
            typeof value.placement === "number" && typeof value.displayName === "string" &&
            isNullableString(value.athleteUrl) && isNullableString(value.profileImageUrl) &&
            typeof value.checkedInDays === "number" && typeof value.totalPoints === "number";
    }

    function isCallSlot(value: unknown): value is CallSlot {
        return hasProperties(value, "id", "startsAtUtc") &&
            typeof value.id === "string" && typeof value.startsAtUtc === "string";
    }

    function isPublicCall(value: unknown): value is PublicCall {
        return hasProperties(value, "key", "label", "candidateSlots", "selectedSlot") &&
            typeof value.key === "string" && typeof value.label === "string" &&
            isArrayOf(value.candidateSlots, isCallSlot) &&
            (value.selectedSlot === null || isCallSlot(value.selectedSlot));
    }

    function isParticipantCall(value: unknown): value is ParticipantCall {
        return hasProperties(value, "key", "label", "selectedSlot", "videoCallUrl") &&
            typeof value.key === "string" && typeof value.label === "string" &&
            (value.selectedSlot === null || isCallSlot(value.selectedSlot)) && isNullableString(value.videoCallUrl);
    }

    function isPublicState(value: unknown): value is PublicState {
        return hasProperties(value, "challengeName", "phase", "signupOpen", "startDate", "signupClosesAtUtc", "callSelectionClosesAtUtc", "endDate", "durationDays", "dailyMaxScore", "days", "leaderboard", "podium", "notes", "calls", "slackInviteUrl", "slackRoomUrl") &&
            typeof value.challengeName === "string" && typeof value.phase === "string" &&
            typeof value.signupOpen === "boolean" && typeof value.startDate === "string" &&
            typeof value.signupClosesAtUtc === "string" && typeof value.callSelectionClosesAtUtc === "string" &&
            typeof value.endDate === "string" && typeof value.durationDays === "number" &&
            typeof value.dailyMaxScore === "number" && isArrayOf(value.days, isDaySummary) &&
            isArrayOf(value.leaderboard, isLeaderboardRow) && isArrayOf(value.podium, isPodiumRow) &&
            isArrayOf(value.notes, isParticipantNote) && isArrayOf(value.calls, isPublicCall) &&
            typeof value.slackInviteUrl === "string" && isNullableString(value.slackRoomUrl);
    }

    function isParticipantSummary(value: unknown): value is ParticipantSummary {
        return hasProperties(value, "id", "email", "displayName", "timeZoneId", "athleteSlug", "athleteUrl", "profileImageUrl", "challengeEmailsStopped", "challengeInactive", "commitmentAmountUsd", "daysIn") &&
            typeof value.id === "string" && typeof value.email === "string" &&
            typeof value.displayName === "string" && typeof value.timeZoneId === "string" &&
            isNullableString(value.athleteSlug) && isNullableString(value.athleteUrl) &&
            isNullableString(value.profileImageUrl) && typeof value.challengeEmailsStopped === "boolean" &&
            typeof value.challengeInactive === "boolean" && isNullableNumber(value.commitmentAmountUsd) &&
            typeof value.daysIn === "number";
    }

    function isCommitmentState(value: unknown): value is CommitmentState {
        return hasProperties(value, "status", "blocksParticipant", "canEditAmount", "canPay", "amountUsd", "owedAmountUsd", "triggerChallengeDay", "triggerScore", "thresholdAverage", "invoiceId", "checkoutLink", "invoiceStatus", "message") &&
            typeof value.status === "string" && typeof value.blocksParticipant === "boolean" &&
            typeof value.canEditAmount === "boolean" && typeof value.canPay === "boolean" &&
            isNullableNumber(value.amountUsd) && isNullableNumber(value.owedAmountUsd) &&
            isNullableNumber(value.triggerChallengeDay) && isNullableNumber(value.triggerScore) &&
            isNullableNumber(value.thresholdAverage) && isNullableString(value.invoiceId) &&
            isNullableString(value.checkoutLink) && isNullableString(value.invoiceStatus) &&
            isNullableString(value.message);
    }

    function isCommitmentTrendGuidance(value: unknown): value is CommitmentTrendGuidance {
        return hasProperties(value, "enforced", "priorScoredDays", "averagePoints", "neededPoints", "text") &&
            typeof value.enforced === "boolean" && typeof value.priorScoredDays === "number" &&
            isNullableNumber(value.averagePoints) && isNullableNumber(value.neededPoints) && typeof value.text === "string";
    }

    function isParticipantState(value: unknown): value is ParticipantState {
        return hasProperties(value, "public", "participant", "eligibleDays", "notes", "calls", "commitment", "trendGuidance") &&
            isPublicState(value.public) && isParticipantSummary(value.participant) &&
            isArrayOf(value.eligibleDays, isEligibleDay) && isArrayOf(value.notes, isParticipantNote) &&
            isArrayOf(value.calls, isParticipantCall) && isCommitmentState(value.commitment) &&
            isCommitmentTrendGuidance(value.trendGuidance);
    }

    function isSignupResult(value: unknown): value is SignupResult {
        return hasProperties(value, "message") && typeof value.message === "string";
    }

    function isAccessResult(value: unknown): value is AccessResult {
        return hasProperties(value, "accessToken", "state") &&
            typeof value.accessToken === "string" && isParticipantState(value.state);
    }

    function invalidApiResponse(url: string): Error {
        return new Error(`${url} returned an unexpected response shape.`);
    }

    async function getJson(url: `${typeof API}/state`): Promise<PublicState>;
    async function getJson(url: string): Promise<unknown>;
    async function getJson(url: string): Promise<unknown> {
        const response = await requestJson(url, { headers: { "Accept": "application/json" } });
        const data = await readJsonResponse(response, url);
        if (url === `${API}/state` && !isPublicState(data)) throw invalidApiResponse(url);
        return data;
    }

    async function postJson(url: `${typeof API}/signup` | `${typeof API}/resend`, payload: object): Promise<SignupResult>;
    async function postJson(url: `${typeof API}/confirm`, payload: object): Promise<AccessResult>;
    async function postJson(
        url: `${typeof API}/edit` | `${typeof API}/participant` | `${typeof API}/commitment-payment` | `${typeof API}/commitment-payment/status` | `${typeof API}/check-in`,
        payload: object
    ): Promise<ParticipantState>;
    async function postJson(url: `${typeof API}/stop-emails`, payload: object): Promise<unknown>;
    async function postJson(url: string, payload: object): Promise<unknown>;
    async function postJson(url: string, payload: object): Promise<unknown> {
        const response = await requestJson(url, {
            method: "POST",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(payload)
        });
        const data = await readJsonResponse(response, url);
        if ((url === `${API}/signup` || url === `${API}/resend`) && !isSignupResult(data)) throw invalidApiResponse(url);
        if (url === `${API}/confirm` && !isAccessResult(data)) throw invalidApiResponse(url);
        if ((url === `${API}/edit` || url === `${API}/participant` || url === `${API}/commitment-payment` || url === `${API}/commitment-payment/status` || url === `${API}/check-in`) && !isParticipantState(data)) {
            throw invalidApiResponse(url);
        }
        return data;
    }

    async function postForm(url: `${typeof API}/check-in` | `${typeof API}/profile-picture`, formData: FormData): Promise<ParticipantState> {
        const response = await requestJson(url, {
            method: "POST",
            headers: { "Accept": "application/json" },
            body: formData
        });
        const data = await readJsonResponse(response, url);
        if (!isParticipantState(data)) throw invalidApiResponse(url);
        return data;
    }

    async function requestJson(url: string, options: RequestInit): Promise<Response> {
        const controller = typeof AbortController !== "undefined" ? new AbortController() : null;
        const timer = controller
            ? window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
            : null;

        try {
            return await fetch(url, {
                ...(options || {}),
                ...(controller ? { signal: controller.signal } : {})
            });
        } catch (err) {
            if (hasStringProperty(err, "name") && err.name === "AbortError") throw new Error("Request timed out");
            throw err;
        } finally {
            if (timer) window.clearTimeout(timer);
        }
    }

    async function readJsonResponse(response: Response, url: string): Promise<unknown> {
        const text = await response.text();
        const contentType = response.headers.get("content-type") || "";
        let data: unknown = {};
        if (text) {
            try {
                data = JSON.parse(text);
            } catch (_) {
                const target = String(url || response.url || "request");
                const typeLabel = contentType ? ` (${contentType})` : "";
                throw new Error(`${target} returned ${response.status || "a non-JSON response"}${typeLabel}.`);
            }
        }

        if (!response.ok) {
            const fallback = response.statusText || (response.status ? `HTTP ${response.status}` : "Request failed");
            const message = typeof data === "string" && data.trim()
                ? data.trim()
                : hasStringProperty(data, "message") && data.message.trim()
                    ? data.message.trim()
                    : Array.isArray(data)
                        ? data.filter(value => typeof value === "string").map(value => value.trim()).filter(Boolean).join("\n")
                        : "";
            throw Object.assign(new Error(message || fallback), { status: response.status });
        }
        return data;
    }

    function hasStringProperty<K extends string>(value: unknown, key: K): value is object & { [P in K]: string } {
        return hasProperties(value, key) && typeof value[key] === "string";
    }

    function isAuthFailure(err: unknown): boolean {
        if (!(err instanceof Error) || !hasProperties(err, "status")) return false;
        const status = err.status;
        return status === 401 || status === 403;
    }

    async function withButton(button: HTMLButtonElement | null, work: ButtonWork, busyText: string): Promise<void> {
        if (!button) return;
        if (button.disabled || button.getAttribute("aria-busy") === "true") return;

        const original = button.innerHTML;
        button.disabled = true;
        button.setAttribute("aria-busy", "true");
        button.innerHTML = `<i class="fas fa-spinner fa-spin" aria-hidden="true"></i>${busyText}`;
        try {
            await work();
        } catch (err) {
            const status = button.closest("form")?.querySelector(".lmx-status");
            if (status) {
                status.textContent = messageOf(err);
                status.classList.add("error");
                status.classList.remove("success");
            }
        } finally {
            button.disabled = false;
            button.removeAttribute("aria-busy");
            button.innerHTML = original;
        }
    }

    function fillTimeZones(select: HTMLSelectElement): void {
        const current = getBrowserTimeZone();
        select.innerHTML = getAvailableTimeZones(current)
            .filter(Boolean)
            .map(zone => `<option value="${escAttr(zone)}">${esc(zone)}</option>`)
            .join("");
        setDefaultTimezone(select);
    }

    function initTimeZonePickers(): void {
        document.querySelectorAll<HTMLElement>("[data-timezone-picker]").forEach(picker => {
            if (picker.dataset.wired === "true") return;
            picker.dataset.wired = "true";
            const button = picker.querySelector(".lmx-timezone-button");
            const input = picker.querySelector<HTMLInputElement>(".lmx-timezone-search input");
            const select = getTimeZoneSelect(picker);

            button?.addEventListener("click", () => toggleTimeZonePicker(picker));
            input?.addEventListener("input", () => renderTimeZoneOptions(picker, input.value));
            input?.addEventListener("keydown", event => handleTimeZoneSearchKeydown(event, picker));
            select?.addEventListener("change", () => syncTimeZonePicker(picker));
            syncTimeZonePicker(picker);
        });

        document.addEventListener("click", event => {
            document.querySelectorAll<HTMLElement>("[data-timezone-picker].open").forEach(picker => {
                if (!(event.target instanceof Node) || !picker.contains(event.target)) closeTimeZonePicker(picker);
            });
        });
    }

    function getTimeZoneSelect(picker: HTMLElement): HTMLSelectElement | null {
        const id = picker?.dataset?.selectId || "";
        return id ? optionalSelect(id) : null;
    }

    function toggleTimeZonePicker(picker: HTMLElement): void {
        if (picker.classList.contains("open")) {
            closeTimeZonePicker(picker);
        } else {
            openTimeZonePicker(picker);
        }
    }

    function openTimeZonePicker(picker: HTMLElement): void {
        document.querySelectorAll<HTMLElement>("[data-timezone-picker].open").forEach(openPicker => {
            if (openPicker !== picker) closeTimeZonePicker(openPicker);
        });
        picker.classList.add("open");
        const button = picker.querySelector(".lmx-timezone-button");
        const popover = picker.querySelector<HTMLElement>(".lmx-timezone-popover");
        const input = picker.querySelector<HTMLInputElement>(".lmx-timezone-search input");
        button?.setAttribute("aria-expanded", "true");
        if (popover) popover.hidden = false;
        if (input) {
            input.value = "";
            renderTimeZoneOptions(picker, "");
            requestAnimationFrame(() => {
                popover?.scrollIntoView({ block: "nearest" });
                input.focus({ preventScroll: true });
            });
        }
    }

    function closeTimeZonePicker(picker: HTMLElement): void {
        picker.classList.remove("open");
        picker.querySelector(".lmx-timezone-button")?.setAttribute("aria-expanded", "false");
        const popover = picker.querySelector<HTMLElement>(".lmx-timezone-popover");
        if (popover) popover.hidden = true;
    }

    function syncTimeZonePicker(picker: HTMLElement): void {
        const select = getTimeZoneSelect(picker);
        const display = picker.querySelector("[data-timezone-display]");
        const offset = picker.querySelector("[data-timezone-offset]");
        const value = select?.value || "UTC";
        if (display) display.textContent = timeZoneDisplayName(value);
        if (offset) offset.textContent = timeZoneOffsetLabel(value);
    }

    function renderTimeZoneOptions(picker: HTMLElement, query: string): void {
        const select = getTimeZoneSelect(picker);
        const list = picker.querySelector(".lmx-timezone-list");
        if (!select || !list) return;
        const selected = select.value || "UTC";
        const terms = normalizeTimeZoneQuery(query).split(" ").filter(Boolean);
        const zones = Array.from(select.options).map(option => option.value).filter(Boolean);
        const matches = zones
            .map(zone => ({ zone, score: timeZoneMatchScore(zone, terms, selected) }))
            .filter(item => item.score > 0)
            .sort((a, b) => b.score - a.score || a.zone.localeCompare(b.zone));

        list.innerHTML = matches.length
            ? matches.map((item, index) => timeZoneOptionHtml(item.zone, selected, index === 0)).join("")
            : `<div class="lmx-timezone-empty">No timezone found</div>`;

        list.querySelectorAll<HTMLElement>(".lmx-timezone-option").forEach(option => {
            option.addEventListener("click", () => chooseTimeZone(picker, option.dataset.timeZone || "UTC"));
        });
    }

    function timeZoneOptionHtml(zone: string, selected: string, active: boolean): string {
        return `<button type="button" class="lmx-timezone-option${active ? " active" : ""}" role="option" data-time-zone="${escAttr(zone)}" aria-selected="${zone === selected ? "true" : "false"}">
            <span>${esc(timeZoneDisplayName(zone))}</span>
            <small>${esc(zone)} · ${esc(timeZoneOffsetLabel(zone))}</small>
        </button>`;
    }

    function chooseTimeZone(picker: HTMLElement, zone: string): void {
        const select = getTimeZoneSelect(picker);
        if (!select) return;
        setSelectValue(select, zone);
        select.dispatchEvent(new Event("change", { bubbles: true }));
        closeTimeZonePicker(picker);
        picker.querySelector<HTMLButtonElement>(".lmx-timezone-button")?.focus();
    }

    function handleTimeZoneSearchKeydown(event: KeyboardEvent, picker: HTMLElement): void {
        const options = Array.from(picker.querySelectorAll<HTMLElement>(".lmx-timezone-option"));
        if (event.key === "Escape") {
            event.preventDefault();
            closeTimeZonePicker(picker);
            picker.querySelector<HTMLButtonElement>(".lmx-timezone-button")?.focus();
            return;
        }
        if (event.key === "Enter") {
            event.preventDefault();
            const active = picker.querySelector<HTMLElement>(".lmx-timezone-option.active") || options[0];
            if (active) chooseTimeZone(picker, active.dataset.timeZone || "UTC");
            return;
        }
        if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
        event.preventDefault();
        if (!options.length) return;
        const current = Math.max(0, options.findIndex(option => option.classList.contains("active")));
        const next = event.key === "ArrowDown"
            ? (current + 1) % options.length
            : (current - 1 + options.length) % options.length;
        options.forEach((option, index) => option.classList.toggle("active", index === next));
        options[next]?.scrollIntoView({ block: "nearest" });
    }

    function getAvailableTimeZones(current: string): string[] {
        const zones = typeof Intl.supportedValuesOf === "function"
            ? Intl.supportedValuesOf("timeZone")
            : FALLBACK_TIME_ZONES;
        const sorted = Array.from(new Set(["UTC", ...zones]))
            .filter(Boolean)
            .sort((a, b) => a.localeCompare(b));
        return Array.from(new Set([current || "UTC", ...sorted]));
    }

    function setDefaultTimezone(select: HTMLSelectElement): void {
        setSelectValue(select, getBrowserTimeZone());
    }

    function getBrowserTimeZone(): string {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
    }

    function setSelectValue(select: HTMLSelectElement, value: string): void {
        const candidate = value || "UTC";
        if (!Array.from(select.options).some(option => option.value === candidate)) {
            const option = document.createElement("option");
            option.value = candidate;
            option.textContent = candidate;
            select.appendChild(option);
        }
        select.value = candidate;
        const picker = Array.from(document.querySelectorAll<HTMLElement>("[data-timezone-picker]"))
            .find(candidatePicker => candidatePicker.dataset.selectId === select.id);
        if (picker) syncTimeZonePicker(picker);
    }

    function timeZoneMatchScore(zone: string, terms: string[], selected: string): number {
        if (!terms.length) return zone === selected ? 1000 : 1;
        const haystack = normalizeTimeZoneQuery(`${zone} ${timeZoneDisplayName(zone)} ${timeZoneCountryLabel(zone, true)}`);
        let score = zone === selected ? 8 : 0;
        for (const term of terms) {
            const index = haystack.indexOf(term);
            if (index < 0) return 0;
            score += index === 0 ? 80 : 50;
            if (haystack.includes(`/${term}`) || haystack.includes(` ${term}`)) score += 25;
        }
        return score;
    }

    function normalizeTimeZoneQuery(value: string): string {
        return String(value || "")
            .toLowerCase()
            .replace(/[_/-]+/g, " ")
            .replace(/[^a-z0-9+ ]+/g, "")
            .trim();
    }

    function timeZoneDisplayName(zone: string): string {
        if (!zone) return "UTC";
        const parts = String(zone).split("/");
        const city = (parts[parts.length - 1] || zone).replace(/_/g, " ");
        const country = timeZoneCountryLabel(zone, false).split(", ")[0] || "";
        return country ? `${city}, ${country}` : city;
    }

    function timeZoneCountryLabel(zone: string, includeAll: boolean): string {
        const codes = getTimeZoneCountryCodes().get(zone) || [];
        const names = codes
            .map(code => {
                try {
                    regionDisplayNames ||= new Intl.DisplayNames(["en"], { type: "region" });
                    return regionDisplayNames.of(code) || code;
                } catch (_) {
                    return code;
                }
            })
            .filter(Boolean);
        if (includeAll) return names.join(", ");
        return names[0] || "";
    }

    function getTimeZoneCountryCodes(): Map<string, string[]> {
        if (!timeZoneCountryCodes) {
            timeZoneCountryCodes = new Map(TIME_ZONE_COUNTRY_DATA.split("|").map(serializedEntry => {
                const [zone, codes] = serializedEntry.split("=");
                const mapEntry: [string, string[]] = [zone || "", String(codes || "").split(",").filter(Boolean)];
                return mapEntry;
            }));
        }
        return timeZoneCountryCodes;
    }

    function timeZoneOffsetLabel(zone: string): string {
        try {
            const parts = new Intl.DateTimeFormat("en-US", {
                timeZone: zone,
                timeZoneName: "shortOffset",
                hour: "2-digit",
                minute: "2-digit"
            }).formatToParts(new Date());
            return parts.find(part => part.type === "timeZoneName")?.value || zone;
        } catch (_) {
            return zone || "UTC";
        }
    }


    function phaseLabel(phase: string | undefined): string {
        switch (phase) {
            case "signup": return "Signup open";
            case "roster": return "Getting ready";
            case "active": return "Live";
            default: return "loading";
        }
    }

    function isPreStartSignup(state: Partial<PublicState>): boolean {
        return !!state && state.phase === "signup";
    }

    function formatDateLabel(value: string): string {
        const date = parseIsoDate(value);
        if (!date) return value || "";
        return new Intl.DateTimeFormat("en-US", { weekday: "long", month: "short", day: "numeric" }).format(date);
    }

    function formatShortDateLabel(value: string): string {
        const date = parseIsoDate(value);
        if (!date) return value || "";
        return new Intl.DateTimeFormat("en-US", { weekday: "short", month: "short", day: "numeric" }).format(date);
    }

    function formatCheckInDate(value: string): string {
        const date = parseIsoDate(value);
        if (!date) return value || "";
        return new Intl.DateTimeFormat("en-US", { weekday: "long", month: "short", day: "numeric" }).format(date);
    }

    function formatWeekday(value: string): string {
        const date = parseIsoDate(value);
        if (!date) return value || "";
        return new Intl.DateTimeFormat("en-US", { weekday: "short" }).format(date);
    }

    function parseIsoDate(value: string | undefined): Date | null {
        const parts = String(value || "").split("-").map(Number);
        if (parts.length !== 3 || parts.some(Number.isNaN)) return null;
        const year = parts[0];
        const month = parts[1];
        const day = parts[2];
        if (year === undefined || month === undefined || day === undefined) return null;
        return new Date(Date.UTC(year, month - 1, day, 12, 0, 0));
    }

    function datePlusDays(value: string, days: number): string {
        const parts = String(value || "").split("-").map(Number);
        if (parts.length !== 3 || parts.some(Number.isNaN)) return value || "";
        const year = parts[0];
        const month = parts[1];
        const day = parts[2];
        if (year === undefined || month === undefined || day === undefined) return value || "";
        const date = new Date(Date.UTC(year, month - 1, day + days, 12, 0, 0));
        return date.toISOString().slice(0, 10);
    }

    function formatDateTime(value: string, timeZoneId: string): string {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return value || "";
        const options: Intl.DateTimeFormatOptions = {
            weekday: "short",
            month: "short",
            day: "numeric",
            hour: "2-digit",
            minute: "2-digit",
            timeZoneName: "short"
        };
        const timeZone = normalizeDisplayTimeZone(timeZoneId);
        if (timeZone) options.timeZone = timeZone;
        return new Intl.DateTimeFormat("en-US", options).format(date);
    }

    function formatCallWhen(value: string, timeZoneId: string): { primary: string; secondary: string } {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return { primary: value || "", secondary: "" };
        const timeZone = normalizeDisplayTimeZone(timeZoneId);
        const primaryOptions: Intl.DateTimeFormatOptions = {
            weekday: "long",
            hour: "numeric",
            minute: "2-digit"
        };
        const secondaryOptions: Intl.DateTimeFormatOptions = {
            month: "short",
            day: "numeric",
            timeZoneName: "short"
        };
        if (timeZone) {
            primaryOptions.timeZone = timeZone;
            secondaryOptions.timeZone = timeZone;
        }
        return {
            primary: new Intl.DateTimeFormat("en-US", primaryOptions).format(date),
            secondary: new Intl.DateTimeFormat("en-US", secondaryOptions).format(date)
        };
    }

    function pendingCallTimeLabel(_callSelectionClosesAtUtc: string, _timeZoneId: string): string {
        return "Meeting time pending.";
    }

    function getParticipantTimeZone(): string {
        return participantState?.participant?.timeZoneId || Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
    }

    function normalizeDisplayTimeZone(timeZoneId: string): string | null {
        const value = String(timeZoneId || "").trim();
        if (!value) return null;
        try {
            new Intl.DateTimeFormat("en-US", { timeZone: value }).format(new Date());
            return value;
        } catch {
            return null;
        }
    }

    function setStatus(id: string, message: string, isError: boolean): void {
        const el = document.getElementById(id);
        if (!el) return;
        el.textContent = message || "";
        el.classList.toggle("error", !!isError);
        el.classList.toggle("success", !isError && !!message);
    }

    function setText(id: string, value: string): void {
        const el = document.getElementById(id);
        if (el) el.textContent = value;
    }

    function toggle(id: string, visible: boolean): void {
        const el = document.getElementById(id);
        if (!el) return;
        el.classList.toggle("lmx-hidden", !visible);
        el.toggleAttribute("hidden", !visible);
    }

    function messageOf(err: unknown): string {
        return hasStringProperty(err, "message") && err.message ? err.message : "Something went wrong.";
    }

    function capitalize(value: string): string {
        return value ? value.charAt(0).toUpperCase() + value.slice(1) : "";
    }

    function esc(value: unknown): string {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function escAttr(value: unknown): string {
        return esc(value);
    }

    function safeStorageGet(key: string): string | null {
        try { return localStorage.getItem(key); } catch (_) { return null; }
    }

    function safeStorageSet(key: string, value: string): void {
        try { localStorage.setItem(key, value); } catch (_) {}
    }

    function safeStorageRemove(key: string): void {
        try { localStorage.removeItem(key); } catch (_) {}
    }
})();
