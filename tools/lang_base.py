#!/usr/bin/env python3
"""다국어 문장의 기준(영어·한국어). assets/lang.json 을 만든다/고친다.
다른 언어(ja, zh-Hans, zh-Hant, es, de, fr, pt, ru)는 lang.json 안에 그대로 두고, 여기 없는 키만 지운다.
   python3 tools/lang_base.py        기준을 lang.json 에 합치고, 빠진 번역을 알려 준다"""
import json, pathlib, sys

LANGS = ['en', 'ko', 'ja', 'zh-Hans', 'zh-Hant', 'es', 'de', 'fr', 'pt', 'ru']
B = {}
def t(key, en, ko): B[key] = {'en': en, 'ko': ko}

# 앱 이름 (트레이·말풍선)
t('app.name', 'Bolok', '볼록이')
# 오른쪽 클릭 메뉴
t('menu.feed', 'Feed', '밥 주기')
t('menu.feed_n', 'Feed ({0}/{1})', '밥 주기 ({0}/{1})')
t('menu.play', 'Play', '놀아주기')
t('menu.sleep', 'Put to bed', '재우기')
t('menu.wake', 'Wake up', '깨우기')
t('menu.shoot', 'Shooting game', '총쏘기 게임')
t('menu.run', 'Running game', '달리기 게임')
t('menu.breakout', 'Breakout', '벽돌깨기')
t('menu.minigun', 'Minigun', '미니건')
t('menu.icbm', 'Launch ICBM', 'ICBM 발사')
t('menu.raid', 'Bug Raid', '버그 습격')
t('menu.swarm', 'Bug Swarm', '버그 떼')
t('menu.showcase', 'Character check (debug)', '캐릭터 확인 (디버그)')
t('menu.showcase_pose', 'Pose', '자세')
t('menu.showcase_auto', 'Auto (all in turn)', '자동 (차례로 모두)')
t('menu.showcase_cheek', 'Cheeks', '볼')
t('menu.showcase_color', 'Color', '색')
t('menu.showcase_end', 'End', '끝내기')
t('menu.fortress', 'Fortress', '포트리스')
t('menu.gun', '{0} (HP {1})', '{0} (HP {1})')
t('menu.gun_shells', '{0} (HP {1}, {2} shells)', '{0} (HP {1}, {2}발)')
t('menu.settings', 'Settings...', '설정...')
t('menu.update', 'Restart with {0}', '새 버전 {0} 으로 다시 시작')
t('menu.quit', 'Quit', '종료')
t('gun.multi', 'Multi-cannon', '멀티캐논')
t('gun.giant', 'Giant cannon', '왕대포')
t('gun.normal', 'Cannon', '일반대포')
# 말풍선
t('say.hatched', '{0} hatched!', '{0} 태어났어요!')
t('say.spit', 'Ptui!', '퉤!')
t('say.no_food', 'The basket is empty… more food will come soon.', '바구니가 비었어요… 조금 있으면 또 생겨요.')
t('say.nowhere', 'Nowhere to drop it', '떨어뜨릴 곳이 없어요')
t('say.no_break', 'No window to make bricks from', '벽돌로 만들 창이 없어요')
t('say.no_gun', 'No window to smash', '부술 창이 없어요')
t('say.no_islands', 'No window to use as islands', '섬이 될 창이 없어요')
t('say.open_window', "Open a window that isn't maximized", '최대화 안 한 창을 하나 띄워 주세요')
# 게임 안내 (처음 몇 초)
t('note.run_level', 'Lv.{0}  Goal {1}', 'Lv.{0}  목표 {1}')
t('note.run_jump', 'Ctrl: Jump', 'Ctrl: 점프')
t('note.run_duck', 'Shift: Duck', 'Shift: 앉기')
t('note.run_quit', 'Hold Ctrl+Shift: Quit', 'Ctrl+Shift 꾹: 종료')
t('note.break_mouse', 'Mouse: Tray', '마우스: 쟁반')
t('note.break_click', 'Click: Throw', '클릭: 던지기')
t('note.esc', 'Esc: Quit', 'Esc: 끝')
t('note.gun_aim', 'Mouse: Aim', '마우스: 조준')
t('note.gun_hold', 'Hold left: Minigun', '왼쪽 누르고 있기: 미니건')
t('note.gun_right', 'Right-click: Grenade', '오른쪽 클릭: 수류탄')
t('note.worms_gun', 'My cannon: {0} (HP {1})', '내 대포: {0} (HP {1})')
t('note.worms_fire', 'Hold and release: Fire', '누르고 있다 놓기: 쏘기')
t('note.worms_walk', '←→: Walk  Space: Jump', '←→: 걷기  Space: 점프')
t('note.worms_wind', 'Up arrow: Wind', '위 화살표: 바람')
t('note.raid_move', '←→ / Mouse: Move', '←→ / 마우스: 움직이기')
t('note.raid_fire', 'Fires automatically', '쏘는 건 자동이에요')
t('note.swarm_move', '←↑↓→: Move', '←↑↓→: 움직이기')
t('note.swarm_auto', 'Gun and sword are automatic', '총과 칼은 자동이에요')
# 단계
t('stage.egg', 'Egg', '알')
t('stage.baby', 'Baby', '아기')
t('stage.child', 'Kid', '어린이')
t('stage.adult_chubby', 'Chubby', '통통이')
t('stage.adult_sweet', 'Sweetie', '애교쟁이')
t('stage.adult_grumpy', 'Grumpy', '심술이')
# 종
for sp, en, ko in [
    ('hamster', 'Hamster', '햄스터'), ('cat', 'Cat', '고양이'),
    ('ragdoll', 'Ragdoll', '렉돌'), ('scottishfold', 'Scottish Fold', '스코티시폴드'), ('exotic', 'Exotic Shorthair', '엑조틱숏헤어'),
    ('shiba', 'Shiba', '시바견'), ('jindo', 'Jindo', '진돗개'), ('malamute', 'Malamute', '말라뮤트'), ('retriever', 'Retriever', '리트리버'),
    ('corgi', 'Welsh corgi', '웰시코기'), ('pug', 'Pug', '퍼그'), ('pomeranian', 'Pomeranian', '포메라니안'), ('fox', 'Fox', '여우'),
    ('moonbear', 'Moon bear', '반달가슴곰'), ('penguin', 'Penguin', '펭귄'), ('owl', 'Owl', '부엉이'), ('squirrel', 'Squirrel', '다람쥐'),
    ('otter', 'Otter', '수달'), ('duck', 'Duck', '오리'), ('frog', 'Frog', '개구리'), ('hedgehog', 'Hedgehog', '고슴도치'),
    ('raccoon', 'Raccoon dog', '너구리'), ('dragon', 'Baby dragon', '아기 용'), ('drake', 'Dragon', '드래곤'), ('sheep', 'Sheep', '양'), ('deer', 'Deer', '사슴'),
    ('goat', 'Goat', '염소'), ('beaver', 'Beaver', '비버'), ('quokka', 'Quokka', '쿼카'), ('capybara', 'Capybara', '카피바라'),
    ('koala', 'Koala', '코알라'), ('panda', 'Panda', '판다'), ('redpanda', 'Red panda', '레서판다'), ('alpaca', 'Alpaca', '알파카'),
    ('pig', 'Pig', '돼지'), ('octopus', 'Octopus', '문어'), ('slime', 'Slime', '슬라임'), ('ghost', 'Ghost', '유령'),
    ('golem', 'Little golem', '꼬마 골렘'), ('unicorn', 'Unicorn', '유니콘'), ('mushroom', 'Mushroom sprite', '버섯 요정'),
    ('imp', 'Little imp', '꼬마 악마'), ('mongma', 'Dream spirit', '몽마'), ('shadowimp', 'Shadow devil', '까망 악마'), ('gumiho', 'Gumiho', '구미호'), ('dokkaebi', 'Little dokkaebi', '꼬마 도깨비')]:
    t('sp.' + sp, en, ko)
# 시간
t('time.min', '{0} min', '{0}분')
t('time.h', '{0} h', '{0}시간')
t('time.hm', '{0} h {1} min', '{0}시간 {1}분')
t('time.d', '{0} d', '{0}일')
t('time.dh', '{0} d {1} h', '{0}일 {1}시간')
# 설정 창
t('set.title', 'Bolok Settings', '볼록이 설정')
t('set.foot', 'Bolok {0}', '볼록이 {0}')
t('set.dev', 'Dev build', '개발 빌드')
t('page.status', 'Status', '상태')
t('page.book', 'Collection', '도감')
t('page.memories', 'Memories', '추억')
t('page.general', 'General', '일반')
t('page.update', 'Updates', '업데이트')
# 상태
t('st.condition', 'Condition', '컨디션')
t('st.condition_foot', 'Below 25, Bolok calls you with a speech bubble.', '25 아래로 내려가면 볼록이가 말풍선으로 불러요.')
t('st.full', 'Fullness', '포만')
t('st.full_desc', 'Fills up when you feed it', '밥을 주면 차요')
t('st.mood', 'Mood', '기분')
t('st.mood_desc', 'Goes up when you play with it or pet it', '놀아 주거나 쓰다듬으면 올라요')
t('st.energy', 'Energy', '기운')
t('st.energy_desc', 'Recovers after sleep', '자고 나면 차요')
t('st.care', 'Care', '돌봄')
t('st.miss', 'Care misses', '돌봄 실수')
t('st.miss_desc', 'Goes up when it stays hungry or bored for over 15 minutes', '배고프거나 심심한 채로 15분 넘게 두면 하나씩 늘어요')
t('st.miss_value', '{0}', '{0}번')
t('st.bites', 'Screen bits in its cheeks', '볼 속 화면 조각')
t('st.bites_desc', 'When grumpy, it nibbles your screen. Feed it and it spits them back. Total so far: {0}', '기분이 나쁘면 화면을 갉아 먹어요. 밥을 주면 뱉어 내요. 지금까지 {0}입')
t('st.bites_value', '{0}', '{0}입')
t('st.care_foot', '8 care misses, or more than 12 screen bites in total, and it grows up Grumpy. Much more petting than feeding makes a Sweetie; otherwise it becomes Chubby.',
  '돌봄 실수가 8번이 되거나 화면을 모두 12입 넘게 먹으면 심술이로 자라요. 밥보다 쓰다듬기를 훨씬 많이 하면 애교쟁이, 아니면 통통이가 돼요.')
t('st.games', 'Game records', '게임 기록')
t('st.unknown', 'What will hatch?', '무엇이 나올까요?')
t('st.age', 'Age {0}', '나이 {0}')
t('chip.hatching', 'Hatching soon', '곧 깨어나요')
t('chip.asleep', 'Sleeping', '쿨쿨 자는 중')
t('chip.hungry', 'Hungry', '배고파요')
t('chip.bored', 'Bored', '심심해요')
t('chip.sleepy', 'Sleepy', '졸려요')
t('chip.happy', 'Happy', '기분 좋아요')
t('chip.ok', 'Doing fine', '잘 지내요')
t('grow.done', 'All grown up', '다 자랐어요')
t('grow.hatch', 'Hatches in {0}', '깨어나기까지 {0}')
t('grow.child', 'Kid in {0}', '어린이까지 {0}')
t('grow.adult', 'Grown up in {0}', '어른까지 {0}')
t('grow.waiting', "Getting the new friend's picture…", '깨어날 친구 그림을 받는 중이에요')
t('game.never', 'Not played yet', '아직 안 했어요')
t('game.raid', 'Bug Raid', '버그 습격')
t('game.raid_val', 'Best {0} · Lv.{1}', '최고 {0}점 · Lv.{1}')
t('game.swarm', 'Bug Swarm', '버그 떼')
t('game.swarm_val', 'Best {0} · {1} bugs', '최고 {0} · {1}마리')
t('game.run', 'Running', '달리기')
t('game.run_val', 'Lv.{0} · best {1}', 'Lv.{0} · 최고 {1}점')
t('game.breakout', 'Breakout', '벽돌깨기')
t('game.breakout_val', 'Best {0} tiles', '최고 {0}조각')
t('game.fortress', 'Fortress', '포트리스')
t('game.fortress_val', '{0} wins', '{0}번 이겼어요')
t('game.fortress_none', 'No wins yet', '아직 못 이겼어요')
t('game.shoot', 'Shooting', '총쏘기')
t('game.shoot_val', 'Best {0} hits', '최고 {0}번 맞혔어요')
# 도감
t('bk.desc', "You won't know who it is until the egg hatches. Friends you've met are shown clearly; the rest are faded.",
  '알이 깨지기 전엔 누가 나올지 몰라요. 만나 본 친구는 또렷하게, 아직 못 만난 친구는 흐리게 보여요.')
t('bk.now', 'Your pet now', '지금 키우는 아이')
t('bk.book', 'Collection  {0} / {1}', '도감  {0} / {1}')
t('bk.gone', 'Friends who left', '떠난 친구들')
t('bk.release', 'Release…', '풀어주기…')
t('bk.egg_desc', "Once it hatches, it's added to your collection", '깨어나면 누가 나왔는지 도감에 올라가요')
t('bk.release_desc', "Released friends go back to nature and a new egg arrives. They won't live with you again, but they'll visit now and then",
  '풀어주면 자연으로 떠나고 그 자리에 새 알이 와요. 다시 같이 살 수는 없지만 가끔 놀러 와요')
t('bk.none_gone', 'No friends have left yet', '아직 떠난 친구가 없어요')
t('bk.left_on', 'Left on {0}', '{0}에 떠났어요')
t('bk.together', 'Together for {0}', '함께한 시간 {0}')
t('bk.visited_on', 'Visited on {0}', '{0}에 놀러 왔어요')
t('bk.more_gone', '{0} earlier friends are not shown.', '그 전에 떠난 친구 {0}명은 줄였어요.')
t('bk.confirm', "Really let {0} go back to nature? It won't live with you again", '정말 {0} 보내 줄까요? 다시 같이 살 수는 없어요')
t('bk.cancel', 'Cancel', '취소')
t('bk.send', 'Let go', '보내 주기')
t('bk.leaving', 'Heading back to nature… a new egg is on its way', '자연으로 떠나는 중이에요… 곧 새 알이 와요')
t('bk.cant', "Can't release right now. Try again after the game.", '지금은 풀어줄 수 없어요. 게임이 끝나면 다시 해 주세요')
t('bk.now_pill', 'Now', '지금')
# 일반
t('gen.language', 'Language', '언어')
t('gen.sound', 'Sound', '소리')
t('gen.sound_foot', 'Off by default.', '기본은 꺼져 있어요.')
t('gen.game_sound', 'Game sounds', '게임 소리')
t('gen.game_sound_desc', 'Sound effects in games like shooting and minigun', '총쏘기·미니건 같은 게임의 효과음이에요')
t('gen.how', 'How to play with Bolok', '볼록이와 노는 법')
t('gen.how1', 'Feed · Play · Games', '밥 주기 · 놀아주기 · 게임')
t('gen.how1_desc', 'Right-click Bolok to open the menu', '볼록이를 오른쪽 클릭하면 메뉴가 나와요')
t('gen.how2', 'Pick up and throw', '잡아서 던지기')
t('gen.how2_desc', 'Drag and let go to toss it. Gentle throws are fun; hard ones make it dizzy', '끌었다가 놓으면 날아가요. 살살 던지면 좋아하고, 세게 던지면 어지러워해요')
t('gen.how3', 'See its status', '상태 보기')
t('gen.how3_desc', "Click Bolok's icon in the taskbar tray to open the status page", '작업 표시줄 오른쪽의 볼록이 아이콘을 누르면 이 창의 상태가 열려요')
t('gen.how4', 'Get your screen back', '먹은 화면 돌려받기')
t('gen.how4_desc', 'Feed it and it spits out the screen bits it was keeping in its cheeks', '밥을 주면 볼 속에 넣어 둔 화면 조각을 뱉어 내요')
# 업데이트
t('up.version', 'Version', '버전')
t('up.current', 'Current version', '지금 버전')
t('up.check', 'Check now', '지금 확인')
t('up.check_label', 'Check for updates', '새 버전 확인')
t('up.check_desc', "Only checks the repository for a new version. Doesn't download anything", '저장소에 새 버전이 있는지 확인만 해요. 내려받지는 않아요')
t('up.restart', 'Restart', '다시 시작')
t('up.ready', 'New version downloaded', '새 버전을 받아 뒀어요')
t('up.ready_tag', '{0} is downloaded', '새 버전 {0} 을 받아 뒀어요')
t('up.ready_desc', 'Restart to switch to the new version', '다시 시작하면 새 버전으로 바뀌어요')
t('up.manual', 'Download manually', '직접 받기')
t('up.releases', 'Releases page', '릴리스 페이지')
t('up.open', 'Open', '열기')
t('up.releases_desc', 'Download Bolok.exe from the repository and replace the old file', '저장소에서 Bolok.exe 를 내려받아 쓰던 파일에 덮어쓰면 설치돼요')
t('up.auto', 'Auto-update', '자동 업데이트')
t('up.auto_desc', 'Checks for a new version once a day and downloads it. Off by default', '켜면 하루에 한 번 새 버전을 확인해서 받아 둬요. 기본은 꺼져 있어요')
t('up.auto_foot', 'Downloads only from github.com/{0}. The file size and fingerprint (sha256) are checked, and the new version takes over the next time Bolok starts.',
  'github.com/{0} 에서만 받아요. 받은 파일의 크기와 지문(sha256)을 확인한 뒤, 다음에 볼록이를 켤 때 새 버전으로 바뀌어요.')
t('up.checking', 'Checking…', '확인하는 중…')
t('up.latest', "You're up to date", '지금이 최신 버전이에요')
t('up.new', '{0} is available. Get it from the releases page, or turn on auto-update', '새 버전 {0} 이 있어요. 릴리스 페이지에서 받거나 자동 업데이트를 켜 주세요')
t('up.fail', "Couldn't check. Please check your internet connection", '확인하지 못했어요. 인터넷 연결을 봐 주세요')
# 추억
t('mem.desc', 'Little things you collect together. Everything here is pictures only.', '함께 모은 작은 것들이에요.')
t('mem.gifts', 'Gifts from friends', '친구들이 두고 간 선물')
t('mem.gifts_none', 'Nothing yet. Visiting friends sometimes leave a gift behind', '아직 없어요. 놀러 온 친구가 가끔 두고 가요')
t('mem.album', 'Growth album', '성장 앨범')
t('mem.album_none', 'A photo is taken every time your pet grows', '자랄 때마다 사진이 찍혀요')
t('mem.cards', 'Postcards', '엽서')
t('mem.cards_none', 'Friends who visited sometimes send one the next day', '놀러 왔던 친구가 다음 날 가끔 보내요')
t('bk.hearts', 'Friendship', '우정')

def main():
    path = pathlib.Path(__file__).resolve().parent.parent / 'assets/lang.json'
    old = json.loads(path.read_text(encoding='utf-8')) if path.exists() else {}
    out = {}
    for key, base in B.items():
        e = {l: (old.get(key, {}).get(l) if l not in base else base[l]) for l in LANGS}
        if old.get(key, {}).get('en') not in (None, base['en']):  # 영어가 바뀌면 다른 언어 번역은 다시 해야 한다
            e = {l: (base[l] if l in base else None) for l in LANGS}
        out[key] = {l: v for l, v in e.items() if v is not None}
    path.write_text(json.dumps(out, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    missing = {l: [k for k, v in out.items() if l not in v] for l in LANGS}
    print(f'{len(out)}개 문장', ', '.join(f'{l}: 빠짐 {len(m)}' for l, m in missing.items() if m) or '모두 번역됨')
    if '--missing' in sys.argv:
        for l, m in missing.items():
            if m: print(l, ' '.join(m))

if __name__ == '__main__':
    main()
