import { getSignInList } from "@/apis/signin";
import { injectGlobalData } from "@/provide";
import { computed, inject, nextTick, provide, reactive, ref } from "vue";

const deviceSymbol = Symbol();
let pageCache = {
    List: [],
    Request:{
        Page: 1, Size: 255, Name: '', Ids: [], Prop: '', Asc: true
    },
    Count:0
};
export const provideDevices = () => {
    //https://api.ipbase.com/v1/json/8.8.8.8
    const globalData = injectGlobalData();
    const machineId = computed(() => globalData.value.config.Client.Id);
    const hasFullList = computed(() => globalData.value.hasAccess('FullList'));

    const ps = +(localStorage.getItem('ps') || '255');
    const count = +(localStorage.getItem('device-count') || '255');
    const prop = localStorage.getItem('prop') || '';
    const asc =  (localStorage.getItem('asc') || 'false') == 'true';
    const name = localStorage.getItem('search-name') || '';
    
    const devices = reactive({
        timer: 0,
        timer1: 0,
        page: {
            Request: {
                Page: 1, Size: ps, Name: name, Ids: [], Prop: prop, Asc: asc
            },
            Count: count,
            List: Array(count).fill().map(c=>{ return {}})
        },
        loadTimer:0,

        showDeviceEdit: false,
        showAccessEdit: false,
        deviceInfo: null
    });

    provide(deviceSymbol, devices);

    const hooks = {};
    const deviceAddHook = (name,dataFn,processFn,refreshFn) => {
        hooks[name] = {dataFn,processFn,refreshFn,changed:true,refresh:true};
    }
    const deviceRefreshHook = (name) => {
        if(hooks[name]) {
            hooks[name].refresh = true;
            hooks[name].changed = true;
        }
    }
    const startHooks = () => { 

        const dataFn = (hook)=>{
            return new Promise((resolve, reject) => { 
                hook.dataFn(devices.page.List.filter(c=>c)).then(changed=>{
                    hook.changed = hook.changed ||changed;
                    resolve();
                });
            });
        }
        const fn = async ()=>{
            clearTimeout(devices.timer1);

            const refreshs = Object.values(hooks).filter(c=>c.refresh);
            refreshs.forEach(hook=>{ 
                hook.refresh = false;
                hook.refreshFn(devices.page.List);
            });

            const changeds = Object.values(hooks).filter(c=>c.changed);
            changeds.forEach(hook=>{ hook.changed=false });
            if(changeds.length > 0){
                for (let i = 0; i< devices.page.List.length; i++) {
                    const device = devices.page.List[i];
                    if(device){
                        const json = {_index:i};
                        for(let j = 0; j < changeds.length; j++) {
                            const hook = changeds[j];
                            hook.processFn(device,json);
                        }
                        Object.assign(device, json);
                    }
                }
                handleSort();
            }
            
            await Promise.all(Object.values(hooks).map(hook=>dataFn(hook)));
            devices.timer1 = setTimeout(fn,1000);
        }
        fn();
    }
    startHooks();

    const deviceStartProcess = () => { 
        _getSignList().then(()=>{
            startHooks();
            _getSignList1();
        });
    }

    const _getCacheOrRemote = ()=>{
        return new Promise((resolve, reject) => { 
            if(pageCache.List && pageCache.List.length > 0){
                resolve(pageCache);
            }else{
                getSignInList(devices.page.Request).then((res)=>{
                    resolve(res);
                }).catch(()=>{});
            }
        });
    }
    const _getSignList = () => {
        return new Promise((resolve, reject) => { 
            _getCacheOrRemote().then((res) => {
                
                if(!hasFullList.value)
                {
                    res.List = res.List.filter(c=>c.MachineId == machineId.value);
                    res.Count = 1;
                }
                devices.page.Request = res.Request;
                devices.page.Count = res.Count;
                
                for (let j in res.List) {
                    const item = res.List[j];
                    Object.assign(item, {
                        showDel: machineId.value != item.MachineId && item.Connected == false,
                        showAccess: machineId.value != item.MachineId && item.Connected,
                        showReboot: item.Connected,
                        isSelf: machineId.value == item.MachineId,
                        avatar: item.Args['avatar'] || '{}',
                        animationDelay: j*50
                    });
                    if (item.isSelf) {
                        globalData.value.self = item;
                    }
                    if(item.avatar.startsWith('http') == false){    
                        try{
                            item.avatar_ = JSON.parse(item.avatar || "{}");
                            item.avatar_style = `
                            line-height: 0;
                            font-family:${item.avatar_.ff || 'auto'};
                            font-size:${item.avatar_.fs||'none'};
                            color:${item.avatar_.fc || 'none'};
                            background-color:${item.avatar_.bc || 'none'}
                            `;
                            item.avatar_text = item.avatar_.ft ||  item.MachineName.split('')[0];
                        }catch(e){}
                    }else{
                        item.avatar_url = item.avatar;
                    }
                    
                }
                devices.page.List = res.List;
                for(let name in hooks) {
                    hooks[name].changed = true;
                }
                
                localStorage.setItem('device-count',devices.page.Count);
                nextTick(()=>{
                    window.dispatchEvent(new Event('resize'));
                });
                resolve()
            }).catch((err) => { resolve() });
        });
    }
    const _getSignList1 = () => {
        clearTimeout(devices.timer);
        getSignInList(devices.page.Request).then((res) => {
            for (let j in res.List) {
                const item = devices.page.List.filter(c => c.MachineId == res.List[j].MachineId)[0];
                if (item) {
                    Object.assign(item, {
                        Connected: res.List[j].Connected,
                        Version: res.List[j].Version,
                        LastSignIn: res.List[j].LastSignIn,
                        Args: res.List[j].Args,
                        showDel: machineId.value != res.List[j].MachineId && res.List[j].Connected == false,
                        showAccess: machineId.value != res.List[j].MachineId && res.List[j].Connected,
                        showReboot: res.List[j].Connected,
                        isSelf: machineId.value == res.List[j].MachineId,
                    });
                    if (item.isSelf) {
                        globalData.value.self = item;
                    }
                }
            }
            handleSort();
            devices.timer = setTimeout(_getSignList1, 5000);
        }).catch((err) => {
            devices.timer = setTimeout(_getSignList1, 5000);
        });
    }
    const handlePageChange = (page) => {
        if (page) {
            devices.page.Request.Page = page;
        }
        localStorage.setItem('search-name',devices.page.Request.Name || '');
        clearTimeout(devices.loadTimer);
        devices.loadTimer = setTimeout(_getSignList,300);
    }
    const handlePageSizeChange = (size) => {
        if (size) {
            devices.page.Request.Size = size;
            localStorage.setItem('ps', size);
        }
        clearTimeout(devices.loadTimer);
        devices.loadTimer = setTimeout(_getSignList,300);
    }
    const deviceClearTimeout = () => {
        clearTimeout(devices.timer);
        clearTimeout(devices.timer1);
        devices.timer = 0;
        devices.timer1 = 0;
    }

    const handleSort = ()=>{
        const prop = devices.page.Request.Prop;
        const asc = devices.page.Request.Asc;
        let list = devices.page.List;
        switch(prop){
            case 'machineName':
                list = asc 
                ? list.sort((a,b)=> a.MachineName.localeCompare(b.MachineName))
                : list.sort((a,b)=> b.MachineName.localeCompare(a.MachineName));
            break;
            case 'tunnel':
                list = asc 
                ? list.sort((a,b)=> a.hook_tunnel_sort - b.hook_tunnel_sort )
                : list.sort((a,b)=> b.hook_tunnel_sort - a.hook_tunnel_sort);
            break;
            case 'tuntap':
                list = asc
                ? list.sort((a,b)=> a.hook_tuntap_sort.localeCompare(b.hook_tuntap_sort))
                : list.sort((a,b)=> b.hook_tuntap_sort.localeCompare(a.hook_tuntap_sort));            
            break;
            default:
                   
            break;

        }
        list =  list.sort((a,b)=> b.Connected - a.Connected);    
        devices.page.List = list;
        pageCache = devices.page;
    }
    const setSort = () => {
        localStorage.setItem('prop',devices.page.Request.Prop);
        localStorage.setItem('asc',devices.page.Request.Asc);
        handleSort();
    }

    return {
        devices,deviceAddHook,deviceRefreshHook, deviceStartProcess, handlePageChange, handlePageSizeChange, deviceClearTimeout, setSort
    }
}
export const useDevice = () => {
    return inject(deviceSymbol);
}