# Ruleset
The Longevity World Cup is a competition between longevity athletes. The goal is to improve the results of biological aging clocks.

## Schedule
- Every year a different competition starts where a different biological aging clock is used for the competition. We call this a **Season**. This is to keep things fresh and exciting, while also keeping up with research on biological aging measurements. In 2025, this aging clock is [PhenoAge](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf), which can be derived from certain blood biomarkers.
- Each season starts and ends in around the middle of January, however test results are valid only from the given calendar year: from January 1st to December 31st.

![image](https://github.com/user-attachments/assets/337ab8a6-935b-4986-8e63-28aa6f494582)

## Point System
- In LWC2025, what counts is the highest biological age reversal: the larger the difference between your biological and chronological age, the higher you rank.
- You can submit as many tests as you want. For your ranking, the PhenoAge is calculated by taking the best pheno age across all your submissions throughout the year. Partial or non-same day submissions are not allowed. If cost is a concern, you can choose to submit just one test during the year at the best possible time, allowing you to participate without significant financial burden. However, if you're addicted to getting your blood drawn, you can theoretically also be submitting a test result every single day of the year.
- In LWC2025 various leagues are also in effect. You might be doing average in the ultimate league and you could still be winning the league of your generation.

![image](https://github.com/user-attachments/assets/968fc0b2-3389-40a3-93e9-4a415f565b11)

## Prizes and Payouts
- In LWC2025 prize money is given to the top 3 athletes in the ultimate league.
- The prize money pool is coming from Bitcoin donations and you are encouraged to contribute.
- 10% of the donations go toward covering organizational troubles, while 90% fund the prize money pool.
- Payouts happen in the middle of January in Bitcoin. We'll help you set up a Bitcoin wallet if you don't already have one. We generally recommend [Green Wallet](https://blockstream.com/green/) for Mobile or [Wasabi Wallet](https://wasabiwallet.io/) for desktop. Funfact: Wasabi Wallet was created by the same developer who created the Longevity World Cup.

![image](https://github.com/user-attachments/assets/9a41f400-92a1-496d-8553-b727186580b2)

## FAQ

### General Questions
#### Who can participate in the Longevity World Cup? 
Anyone interested in longevity and capable of submitting valid test results can participate.

#### How do I register for the competition?
Simply visit our [website](https://www.longevityworldcup.com/) and follow the registration instructions.

![image](https://github.com/user-attachments/assets/38c545e9-13e5-4ba2-b2e0-d52bbf149207)

Want someone to hold your hand while doing your application? Watch [this seven minute video tutorial](https://www.youtube.com/watch?v=0mCIbqgfqq8) or [this one minute video tutorial.](https://www.youtube.com/shorts/yhMFZMPAoKQ)

#### Can I withdraw from the competition?
Yes, just send us an email to `longevityworldcup@gmail.com`.

### About PhenoAge and Testing

#### What is PhenoAge? 
[PhenoAge](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf) is a biological age measure based on clinical biomarkers like glucose and CRP. It reflects physiological aging, not just years lived, and helps assess health and disease risk.

#### From which biomarkers can I calculate my PhenoAge?
- Albumin (Serum Albumin)  
- Creatinine (Serum Creatinine)  
- Glucose (Blood Sugar)  
- C-Reactive Protein (CRP or hs-CRP)  
- Lymphocyte Percentage (Lymphocyte % or Absolute Lymphocyte Count)  
- Mean Corpuscular Volume (MCV or Average Red Blood Cell Size)  
- Red Cell Distribution Width (RDW, RDW-CV)  
- Alkaline Phosphatase (ALP, Alk Phos)  
- White Blood Cell Count (WBC Count, Leukocyte Count)  

![image](https://github.com/user-attachments/assets/4770485d-440c-4ce6-be6a-b547798696c3)

#### Why did you choose PhenoAge for 2025 World Cup?
[PhenoAge](https://pmc.ncbi.nlm.nih.gov/articles/PMC5940111/pdf/aging-10-101414.pdf) can be acquired from traditional blood biomarkers. To kick off the very first Longevity World Cup, creating a low barrier of entry is paramount.

#### Can I use any laboratory for my tests in LWC2025?
Yes, as long as the lab provides accurate blood biomarkers required for PhenoAge calculation.

#### Why does my PhenoAge result differ from other calculators?
The Longevity World Cup pheno age calculator is the best pheno age calculator on the Internet. Many online PhenoAge calculators are using an [incorrect constant in the formula,](https://github.com/ajsteele/bioage/issues/3) which originated from typo in an update by the authors of PhenoAge. Even those calculators that use the correct constant are inferior to the Longevity World Cup's algorithm, because they reward PhenoAge-optimizing hacks that reduce mortality risk in the model but increase actual mortality in reality. For example, pushing alkaline phosphatase or RDW to the extremes can lower your PhenoAge score even though real-world data shows U- or J-shaped mortality curves for those biomarkers. The Longevity World Cup calculator corrects this by enforcing biologically justified cutoffs, avoiding strategies that make your “age” look better while your actual risk gets worse. See [PhenoAge Calculation Bug Disclosure: Missing U-Shaped Curves for Biomarkers](https://github.com/nopara73/LongevityWorldCup/issues/136).
### Competition Mechanics
#### What happens if my results arrive late?
Each season is wrapped up in the middle of January. This should give your laboratory enough time to evaluate your test conducted on December 31.

#### What if there's a tie?
The older you are, the higher you rank in case of a tie. If necessary, alphabetical ordering of usernames will decide.  

![image](https://github.com/user-attachments/assets/a13ec2f2-346e-4024-aba5-dd32e807a34e)

#### How is my PhenoAge calculated if I submit multiple results?
If you submit multiple test results, the best PhenoAge is taken across all your 2025 submissions. This encourages incremental updates and fair comparisons between strategic and transparent participants.

#### How are lab detection limits handled in the competition?
When your lab result shows a biomarker value as below the detection limit the detection limit itself will be used in calculating your PhenoAge. This policy is applied to keep it fair towards other participants. This happens most often with CRP, in that case when the detection limit is unknown we default it to 1 mg/L.

#### How can I cheat?
You can't. 

#### How does the Longevity World Cup compare to the Rejuvenationy Olympics?
- **Focus**: LWC emphasizes **absolute age reversal**, while RO currently measures the **pace of aging** regardless of chronological age.  
- **Structure**: LWC has **annual seasons** with a single-clock focus.  
- **Prizes**: LWC offers **prize money** through Bitcoin donations.  
- **Leagues**: LWC includes **leagues** for generational and other category-based rankings.  
- **Testing**: LWC2025 uses traditional blood-test-based biological age calculations; RO uses the TruDiagnostic home test kit.

### Practical Matters
#### How much can I edit my profile picture?
Your profile picture must be you, facing the camera, but you can edit it freely, even as a drawing or AI-generated version. You are however encouraged to share a picture that best represents you, because of a feature of the website, called Guess My Age, where visitors can guess your age.

![image](https://github.com/user-attachments/assets/613afebb-4ec7-4b0d-a961-8a09e26391ab)

#### I am an athlete already, how can I make changes?
Through the [Athlete Dashboard](https://www.longevityworldcup.com/play/character-selection.html) or by sending us an email to `longevityworldcup@gmail.com`.

#### What will sponsorships entail?
Sponsorships are planned for future seasons, allowing companies to sponsor athletes in exchange for visibility on the website.

#### What happens if Bitcoin's value fluctuates significantly?
[1 BTC = 1 BTC](https://old.reddit.com/r/Bitcoin/comments/w1di0k/please_understand_what_1_btc_1_btc_really_means/)




