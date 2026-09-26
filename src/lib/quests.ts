import { trees as demoTrees } from '@/data/trees';
import { liveText } from '@/i18n/liveGame';
import type { MyClaimDto, QuestDto } from '@/lib/api';
import { genusName, speciesForGenus } from '@/lib/verificationText';
import type { QuestKind, Tree } from '@/types/tree';

const t = liveText.de;

/** Street names for the `street_key` values the demo data already resolved; other keys fall back to "Münster". */
const streetNames = new Map(demoTrees.flatMap((tree) => (tree.inventory ? [[tree.inventory.streetKey, tree.area] as const] : [])));

export const questKind = (taskType: string): QuestKind | null =>
  taskType === 'photo' ? 'photo' : taskType === 'verify_attribute' ? 'verify' : null;

/** Maps an API quest onto the Tree the map, bottom sheet and scan dialog already understand. */
export function questToTree(quest: QuestDto, claim: { id: string; expiresAt: string } | null = null): Tree | null {
  const kind = questKind(quest.taskType);
  if (!kind) return null; // measure and condition_report quests have no flow in the app yet
  const attributes = quest.asset.attributes ?? {};
  const placeholder = attributes.quality_flags?.includes('placeholder_genus') ?? false;
  const genus = !placeholder && attributes.genus ? attributes.genus : null;
  const card = speciesForGenus(genus);
  return {
    id: `quest-${quest.id}`,
    lat: quest.asset.lat,
    lng: quest.asset.lon,
    species: genus ? genusName(genus) : t.quest.unknownGenus,
    speciesLatin: genus ?? attributes.genus_raw ?? '',
    status: 'unverified',
    verificationCount: 0,
    rarity: kind === 'verify' ? 'uncommon' : 'common',
    discovered: false,
    xpReward: quest.rewardPoints,
    area: (attributes.street_key && streetNames.get(attributes.street_key)) || t.quest.area,
    ...(genus ? { inventory: { genus, streetKey: attributes.street_key ?? '' } } : {}),
    quest: {
      id: quest.id,
      kind,
      title: quest.title,
      description: quest.description,
      rewardPoints: quest.rewardPoints,
      freeSlots: quest.freeSlots,
      geofenceRadiusM: quest.geofenceRadiusM,
      attribute: quest.taskConfig?.attribute ?? null,
      assetId: quest.asset.id,
      genus,
      genusRaw: attributes.genus_raw ?? null,
      claim,
    },
    // Card species for the lexicon link; the genus level name stays the title.
    ...(card ? { lexiconSpecies: card } : {}),
  };
}

/** Active claims first (they stay on the map while the nearby query hides them), then open quests without duplicates. */
export function mergeQuestTrees(nearby: QuestDto[], claims: MyClaimDto[]): Tree[] {
  const now = Date.now();
  const active = claims.filter((claim) => claim.status === 'active' && Date.parse(claim.expiresAt) > now);
  const seen = new Set<string>();
  const result: Tree[] = [];
  for (const claim of active) {
    const tree = questToTree(claim.quest, { id: claim.id, expiresAt: claim.expiresAt });
    if (tree && !seen.has(claim.quest.id)) { seen.add(claim.quest.id); result.push(tree); }
  }
  for (const quest of nearby) {
    const tree = seen.has(quest.id) ? null : questToTree(quest);
    if (tree) { seen.add(quest.id); result.push(tree); }
  }
  return result;
}

/** XP that the moderation confirmed and XP still waiting for review. */
export function claimPoints(claims: MyClaimDto[]) {
  let confirmed = 0;
  let pending = 0;
  for (const claim of claims) {
    if (claim.submission?.status === 'approved') confirmed += claim.quest.rewardPoints;
    else if (claim.submission?.status === 'pending') pending += claim.quest.rewardPoints;
  }
  return { confirmed, pending };
}

/** Status key of a claim for the "Meine Quests" list. */
export function claimState(claim: MyClaimDto): 'active' | 'pending' | 'approved' | 'rejected' | 'expired' | 'cancelled' {
  if (claim.submission) return claim.submission.status;
  if (claim.status === 'active' && Date.parse(claim.expiresAt) <= Date.now()) return 'expired';
  return claim.status === 'submitted' ? 'pending' : claim.status;
}
